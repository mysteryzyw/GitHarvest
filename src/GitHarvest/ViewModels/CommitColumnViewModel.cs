using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;

namespace GitHarvest.ViewModels;

/// <summary>
/// 选择提交页里一个提交栏（基准提交 / Head 提交）的列表状态：
/// 搜索过滤（渐进反馈）、滚动增量加载与栏内选中项。
/// 两栏共享同一份分支历史——搜索时用共享的 <see cref="CommitSearchCache"/>
/// （后台整份读一次，之后过滤与翻页都在内存里做），浏览时仍走 git 分页下推。
/// 只依赖 Core 接口，不接触进程与文件系统。
/// </summary>
public sealed partial class CommitColumnViewModel : ObservableObject
{
    /// <summary>每页拉取的提交数：首批与滚动续载同一页大小（滚动一屏几条，100 条足够多次滚动）。</summary>
    private const int PageSize = 100;

    /// <summary>搜索输入的防抖时长：连续击键只在停顿后发起一次全量检索。</summary>
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly IGitService _gitService;
    private readonly CommitSearchCache _cache;

    /// <summary>当前加载的仓库根与引用（分支名 / HEAD）；由宿主在分支变化时调用 <see cref="Reset"/> 设置。</summary>
    private string? _repositoryRoot;
    private string? _reference;

    /// <summary>竞态令牌：每次新查询自增，过期结果（慢查询晚归）直接丢弃。</summary>
    private int _generation;

    /// <summary>浏览态（无搜索词）已加载的提交，同时作为搜索渐进反馈的过滤源。</summary>
    private IReadOnlyList<CommitSummary> _browseItems = [];

    /// <summary>浏览态在 git 侧是否还有下一页（清空搜索词回到浏览态时恢复它）。</summary>
    private bool _browseHasMore;

    /// <summary>搜索态的完整结果（缓存就绪后填充）；<see langword="null"/> 表示仍在渐进阶段。</summary>
    private IReadOnlyList<CommitSummary>? _searchResults;

    /// <param name="gitService">提交读取接口。</param>
    /// <param name="cache">两栏共享的提交搜索缓存（首次搜索后台整份读取，之后零 git 调用）。</param>
    public CommitColumnViewModel(IGitService gitService, CommitSearchCache cache)
    {
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(cache);

        _gitService = gitService;
        _cache = cache;
    }

    /// <summary>栏标题（原型 .pick-head .t）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>栏说明（原型 .pick-head .d）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>角色标识（基准 / Head）：驱动角色图标的底色（基准灰、Head 蓝）。</summary>
    public CommitColumnRole RoleKind { get; init; } = CommitColumnRole.Base;

    /// <summary>已加载的提交（滚动增量追加；搜索时整体重建）。</summary>
    public ObservableCollection<CommitItem> Commits { get; } = [];

    /// <summary>过滤后结果集里是否还有下一页（驱动「滚到底自动续载」）。</summary>
    [ObservableProperty]
    private bool _hasMore;

    /// <summary>正在读取一页提交（浏览态的首批与滚动续载；列表底部给出加载反馈）。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 正在检索全部提交（搜索态的缓存读取中）。
    /// 这段时间列表显示的是「已加载提交里命中的部分」（渐进反馈），检索完成后被完整结果替换。
    /// </summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>栏内选中的提交——即「指定为本栏角色的提交」。</summary>
    [ObservableProperty]
    private CommitItem? _selectedCommit;

    /// <summary>搜索框内容；变化后防抖触发重新查询。</summary>
    [ObservableProperty]
    private string? _searchText;

    /// <summary>首批读取失败的提示（栏内显示；滚动续载失败保持已加载内容并停止续载）。</summary>
    [ObservableProperty]
    private string? _loadErrorMessage;

    /// <summary>是否显示读取失败提示。</summary>
    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorMessage);

    /// <summary>当前是否有效（已设置仓库与引用）；否则列表区显示提示。</summary>
    public bool HasSource => _repositoryRoot is not null && _reference is not null;

    /// <summary>列表为空且有过滤词时的提示文案（与「没有提交」区分开）。</summary>
    public string EmptyText =>
        string.IsNullOrWhiteSpace(SearchText)
            ? "该分支还没有提交。"
            : "没有匹配的提交，换个关键词试试。";

    /// <summary>选中项变化（用户点选或程序性重选）时通知宿主——宿主据此更新详情预览。</summary>
    public event EventHandler<CommitItem?>? SelectionActed;

    /// <summary>
    /// 重置到新的仓库 / 引用：作废在途查询、清空搜索与列表后加载首批。
    /// <paramref name="autoSelectFirst"/> 为真时首批加载完自动选中第一条
    /// （Head 栏的默认：导出终点通常就是分支最新提交）。
    /// </summary>
    public void Reset(string repositoryRoot, string reference, bool autoSelectFirst = false)
    {
        _repositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot) ? null : repositoryRoot;
        _reference = string.IsNullOrWhiteSpace(reference) ? null : reference;
        // 先清搜索文本（若触发防抖排程，其携带的是即将过期的令牌），再自增令牌作废一切在途查询。
        SearchText = null;
        _generation++;
        var generation = _generation;
        _browseItems = [];
        _browseHasMore = false;
        _searchResults = null;
        // 搜索态标志要压在 SearchText 赋值之后复位（上面那次赋值可能把它置真）。
        IsSearching = false;
        Commits.Clear();
        HasMore = false;
        SelectedCommit = null;
        LoadErrorMessage = null;
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(EmptyText));

        // 没有有效的仓库 / 引用（未打开仓库、分支读取失败）时只清空，不发起查询。
        if (!HasSource)
        {
            IsLoading = false;
            return;
        }

        _ = LoadFirstPageCoreAsync(generation, autoSelectFirst);
    }

    /// <summary>
    /// 搜索文本变化：立刻用「已加载的提交」过滤出结果（渐进反馈，毫秒级、零 git 调用），
    /// 再防抖触发一次共享缓存的全量检索；就绪后用完整结果替换渐进结果。
    /// 清空搜索词则回到浏览态（已加载部分原样显示，续载继续走 git 分页）。
    /// </summary>
    partial void OnSearchTextChanged(string? value)
    {
        if (!HasSource)
        {
            return;
        }

        var generation = ++_generation;
        var keyword = value?.Trim();

        if (string.IsNullOrEmpty(keyword))
        {
            _searchResults = null;
            IsSearching = false;
            ShowItems(_browseItems, hasMore: _browseHasMore);
            return;
        }

        // 渐进反馈：缓存已就绪就用它过滤，否则先用已加载的浏览结果顶着（结果可能不全，
        // 由 IsSearching 提示「正在检索全部提交…」）。
        var source = _searchResults ?? CommitSearchFilter.Filter(_browseItems, keyword);
        ShowItems(source.Take(PageSize), hasMore: false);
        IsSearching = true;
        _ = SearchAfterDelayAsync(generation);
    }

    private async Task SearchAfterDelayAsync(int generation)
    {
        await Task.Delay(SearchDebounceDelay).ConfigureAwait(true);
        if (generation != _generation)
        {
            return;
        }

        var keyword = SearchText?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return; // 期间已被清空，OnSearchTextChanged 已处理
        }

        var all = await _cache.GetOrLoadAsync().ConfigureAwait(true);
        if (generation != _generation)
        {
            return;
        }

        _searchResults = CommitSearchFilter.Filter(all, keyword);
        ShowItems(_searchResults.Take(PageSize), hasMore: _searchResults.Count > PageSize);
        IsSearching = false;
        LoadErrorMessage = null;
    }

    /// <summary>
    /// 滚动到底：追加下一页。搜索态从内存中的完整结果切片（零 git 调用）；
    /// 浏览态仍按偏移向 git 取下一页。
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading || !HasSource)
        {
            return;
        }

        if (_searchResults is { } results && !string.IsNullOrEmpty(SearchText?.Trim()))
        {
            AppendItems(results.Skip(Commits.Count).Take(PageSize));
            HasMore = results.Count > Commits.Count;
            return;
        }

        var generation = ++_generation;
        IsLoading = true;
        try
        {
            var result = await _gitService.GetCommitsAsync(
                _repositoryRoot!,
                _reference!,
                new CommitQuery(SearchText?.Trim(), Offset: Commits.Count, Limit: PageSize)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            if (result.Commits is { } nextPage)
            {
                AppendItems(nextPage);
                _browseItems = [.. _browseItems, .. nextPage];
            }

            _browseHasMore = result.HasMore;
            HasMore = result.HasMore;
        }
        catch (OperationCanceledException)
        {
            // 滚动续载没有界面取消入口，窗口关闭场景的兜底。
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFirstPageCoreAsync(int generation, bool autoSelectFirst)
    {
        IsLoading = true;
        try
        {
            var result = await _gitService.GetCommitsAsync(
                _repositoryRoot!,
                _reference!,
                new CommitQuery(SearchText?.Trim(), Offset: 0, Limit: PageSize)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                // 读取失败（分支被删等）：清空列表、把原因摆上栏内提示，而不是静默空白。
                _browseItems = [];
                _browseHasMore = false;
                Commits.Clear();
                HasMore = false;
                LoadErrorMessage = result.FailureMessage;
                return;
            }

            LoadErrorMessage = null;
            _browseItems = result.Commits ?? [];
            _browseHasMore = result.HasMore;
            ShowItems(_browseItems, result.HasMore);

            if (autoSelectFirst && SelectedCommit is null && Commits.Count > 0)
            {
                SelectedCommit = Commits[0];
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>重置列表为给定内容并更新「是否还有下一页」。</summary>
    private void ShowItems(IEnumerable<CommitSummary> items, bool hasMore)
    {
        Commits.Clear();
        AppendItems(items);
        HasMore = hasMore;
    }

    /// <summary>把一批提交追加进列表（显示模型统一在此转换）。</summary>
    private void AppendItems(IEnumerable<CommitSummary> items)
    {
        foreach (var summary in items)
        {
            Commits.Add(CommitItem.FromCommitSummary(summary));
        }
    }

    partial void OnSelectedCommitChanged(CommitItem? value) => SelectionActed?.Invoke(this, value);
}

/// <summary>
/// 提交栏在变更范围里的角色：基准提交（Base）或 Head 提交。
/// 用枚举而非字符串：角色被两处 XAML DataTrigger（图标与徽章底色）匹配，
/// 枚举让拼写错误在编译期暴露而不是静默不命中。
/// </summary>
public enum CommitColumnRole
{
    /// <summary>基准提交：变更范围的起点，其内容代表「更新前」状态。</summary>
    Base,

    /// <summary>Head 提交：变更范围的终点，其内容代表「更新后」状态。</summary>
    Head,
}

/// <summary>
/// 提交列表项的显示模型：在 <see cref="CommitSummary"/> 之上补相对时间文案
/// （原型 .commit .meta 的「2 周前」风格，复用 <see cref="RelativeTimeFormatter"/>）。
/// </summary>
public sealed class CommitItem
{
    private CommitItem(CommitSummary summary, DateTimeOffset now)
    {
        ShortHash = summary.ShortHash;
        Subject = summary.Subject;
        AuthorName = summary.AuthorName;
        AuthorTime = summary.AuthorTime;
        RelativeTimeText = RelativeTimeFormatter.Format(summary.AuthorTime, now);
    }

    /// <summary>短哈希（git 自动判定的无歧义长度）。</summary>
    public string ShortHash { get; }

    /// <summary>提交信息首行。</summary>
    public string Subject { get; }

    /// <summary>作者名。</summary>
    public string AuthorName { get; }

    /// <summary>作者时间（带时区）。</summary>
    public DateTimeOffset AuthorTime { get; }

    /// <summary>相对时间文案（「3 天前」）。</summary>
    public string RelativeTimeText { get; }

    public static CommitItem FromCommitSummary(CommitSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new CommitItem(summary, DateTimeOffset.Now);
    }
}
