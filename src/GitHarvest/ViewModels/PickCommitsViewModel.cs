using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
using Serilog;

namespace GitHarvest.ViewModels;

/// <summary>
/// 选择提交页（工作流第 2 步）的状态：分支选择（本地/远程分组、过滤、游离标注）、
/// 「拉取」刷新、上次选中分支的记忆与自动选中，以及基准/Head 双栏提交列表
/// （搜索过滤、滚动增量加载、单提交详情预览与变更范围指示）。
/// 只依赖 Core 接口——分支、提交与拉取走 <see cref="IGitService"/>，上次分支走
/// <see cref="ISettingsService"/>，当前仓库来自 <see cref="IRepositorySession"/>。
/// </summary>
public sealed partial class PickCommitsViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly ISettingsService _settings;
    private readonly IRepositorySession _session;
    private readonly IShellNavigator _navigator;

    /// <summary>两栏共享的提交搜索缓存（首次搜索后台整份读一次，之后过滤与翻页零 git 调用）。</summary>
    private readonly CommitSearchCache _commitCache;

    /// <summary>全量分支候选项（过滤前的源），过滤投影到 <see cref="BranchGroups"/>。</summary>
    private IReadOnlyList<BranchItem> _allBranches = [];

    public PickCommitsViewModel(
        ShellViewModel shell,
        IGitService gitService,
        ISettingsService settings,
        IRepositorySession session,
        IShellNavigator navigator,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        _gitService = gitService;
        _settings = settings;
        _session = session;
        _navigator = navigator;
        _commitCache = new CommitSearchCache(gitService, logger);

        BaseList = new CommitColumnViewModel(gitService, _commitCache)
        {
            Title = "基准提交（Base）",
            Description = "其内容代表「更新前」状态，本身变更不导出",
            RoleKind = CommitColumnRole.Base,
        };
        HeadList = new CommitColumnViewModel(gitService, _commitCache)
        {
            Title = "Head 提交",
            Description = "其内容代表「更新后」状态，本身变更包含在导出内",
            RoleKind = CommitColumnRole.Head,
        };
        BaseList.SelectionActed += OnColumnSelectionActed;
        HeadList.SelectionActed += OnColumnSelectionActed;
        BaseList.PropertyChanged += OnColumnPropertyChanged;
        HeadList.PropertyChanged += OnColumnPropertyChanged;
        Preview = new CommitPreviewViewModel(gitService);
    }

    /// <summary>外壳状态（页面标题、步骤指示条等）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>基准提交栏：变更范围的起点（「更新前」状态）。</summary>
    public CommitColumnViewModel BaseList { get; }

    /// <summary>Head 提交栏：变更范围的终点（「更新后」状态）。</summary>
    public CommitColumnViewModel HeadList { get; }

    /// <summary>提交详情预览（最后点选的提交的完整信息）。</summary>
    public CommitPreviewViewModel Preview { get; }

    /// <summary>基准与 Head 是否都已指定（驱动范围条与「下一步」的可达性提示）。</summary>
    public bool HasRange => BaseList.SelectedCommit is not null && HeadList.SelectedCommit is not null;

    /// <summary>范围条上基准一侧的短哈希；未指定时为「—」。</summary>
    public string RangeBaseHash => BaseList.SelectedCommit?.ShortHash ?? "—";

    /// <summary>范围条上 Head 一侧的短哈希；未指定时为「—」。</summary>
    public string RangeHeadHash => HeadList.SelectedCommit?.ShortHash ?? "—";

    /// <summary>范围条的状态文案：选齐后说明变更范围的语义（双点 base..head）；
    /// 祖先校验（merge-base）与文件清单属于第 3 步「导出前总预览」，本页不重复做。
    /// </summary>
    public string RangeText =>
        HasRange
            ? $"变更范围 {RangeBaseHash}..{RangeHeadHash}（不含基准、含 Head）。"
            : "请分别在两栏指定基准提交与 Head 提交。";

    /// <summary>当前打开的仓库（来自会话）；未打开时页面显示「先打开仓库」的引导。</summary>
    public RepositoryInfo? Repository => _session.OpenedRepository;

    /// <summary>是否已打开仓库（决定显示分支区还是引导卡）。</summary>
    public bool HasRepository => Repository is not null;

    /// <summary>副标题（原型 page-sub：仓库名 · 操作说明）。</summary>
    public string PageSubtitle =>
        Repository is { } repository
            ? $"{Path.GetFileName(repository.RootPath)} · 选定基准提交与 Head 提交，确定变更范围（base..head）。"
            : "选定基准提交与 Head 提交，确定变更范围（base..head）。";

    /// <summary>按组（本地分支 / 远程分支）投影后的分支列表，已应用过滤。</summary>
    [ObservableProperty]
    private IReadOnlyList<BranchGroup> _branchGroups = [];

    /// <summary>当前选中的分支；未选中时为 <see langword="null"/>。</summary>
    [ObservableProperty]
    private BranchItem? _selectedBranch;

    /// <summary>过滤框内容（按分支名即时过滤，大小写不敏感）。</summary>
    [ObservableProperty]
    private string? _filterText;

    /// <summary>正在加载分支或拉取（命令进行中），期间禁用「拉取」避免重复提交。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>当前空闲；作为「拉取」命令的 CanExecute 条件。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>分支读取或拉取失败的错误提示。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>拉取结果的中性/成功提示（已更新 / 无远程）。</summary>
    [ObservableProperty]
    private string? _infoMessage;

    /// <summary>是否显示错误横幅（有错误文案时）。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>是否显示成功/中性横幅（有提示文案时）。</summary>
    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

    /// <summary>「拉取」按钮文案：进行中给出反馈（原型按钮本身无 loading 态，文案承担）。</summary>
    public string FetchButtonText => IsBusy ? "拉取中…" : "拉取";

    /// <summary>
    /// 页面加载时读取分支列表：成功后自动选中——上次用过的分支优先，
    /// 其次当前检出的分支（用户故事：减少重复操作）。
    /// </summary>
    [RelayCommand]
    private async Task LoadBranchesAsync()
    {
        if (Repository is not { } repository)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var lastBranch = _settings.GetRepositoryState(repository.RootPath).LastBranch;
            await RefreshBranchesAsync(repository.RootPath, lastBranch);
        }
        catch (OperationCanceledException)
        {
            // 页面加载目前没有界面取消入口，这是窗口关闭等场景的兜底：收敛成状态提示。
            ErrorMessage = "读取分支列表的操作被取消。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 「拉取」：git fetch 只更新远程跟踪分支（不 merge、不动工作区），完成后重读分支数据。
    /// 当前选中按名称保留——fetch 不改变本地分支与游离状态。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FetchAsync()
    {
        if (Repository is not { } repository)
        {
            return;
        }

        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            var result = await _gitService.FetchAsync(repository.RootPath);
            if (!result.IsSuccess)
            {
                // 无远程不是错误而是说明（中性提示），其余失败进错误横幅。
                if (result.Failure == FetchFailure.NoRemote)
                {
                    InfoMessage = result.FailureMessage;
                }
                else
                {
                    ErrorMessage = result.FailureMessage;
                }

                return;
            }

            InfoMessage = "远程跟踪分支已更新。";
            // 远程引用更新可能改变所选引用的历史：整份提交缓存失效，下次搜索重新读取。
            _commitCache.Invalidate(repository.RootPath, SelectedBranch?.Name);
            await RefreshBranchesAsync(repository.RootPath, SelectedBranch?.Name);
        }
        catch (OperationCanceledException)
        {
            InfoMessage = "拉取操作被取消。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 重读分支列表并刷新展示投影：优先按 <paramref name="preferredName"/> 回选
    /// （加载时是「上次选中的分支」，拉取后是「当前选中的分支」），
    /// 其次当前检出的分支，最后列表第一项。失败时把提示摆上错误横幅。
    /// </summary>
    private async Task RefreshBranchesAsync(string repositoryRoot, string? preferredName)
    {
        var result = await _gitService.GetBranchesAsync(repositoryRoot);
        if (result is not { Branches: { } branches })
        {
            ErrorMessage = result.FailureMessage;
            return;
        }

        _allBranches = [.. branches.Select(BranchItem.FromBranchInfo)];
        ApplyFilter();
        SelectedBranch =
            _allBranches.FirstOrDefault(branch => preferredName is { Length: > 0 } && branch.Name == preferredName)
            ?? _allBranches.FirstOrDefault(branch => branch.IsCurrent)
            ?? _allBranches.FirstOrDefault();
    }

    /// <summary>「返回：打开仓库」：未打开仓库时引导回工作流第 1 步。</summary>
    [RelayCommand]
    private void GoToRepository() => _navigator.NavigateTo(ShellPage.Repository);

    /// <summary>选中一个分支（下拉项点击）：更新选中态并关闭下拉由视图绑定完成。</summary>
    [RelayCommand]
    private void SelectBranch(BranchItem? branch)
    {
        if (branch is not null)
        {
            SelectedBranch = branch;
        }
    }

    partial void OnSelectedBranchChanged(BranchItem? oldValue, BranchItem? newValue)
    {
        // 项内 ✓ 标记跟着选中走：旧项摘下、新项戴上。
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        // 分支变化同时驱动提交列表：重置双栏到新分支的历史（Head 栏默认选中分支最新提交，
        // 让「导出终点」有合理的起点值；基准留给用户明确指定）。
        if (Repository is { } repository && newValue is not null)
        {
            // 换了分支：两栏共享的提交缓存整份失效（新分支的历史是另一份数据）。
            _commitCache.Invalidate(repository.RootPath, newValue.Name);
            BaseList.Reset(repository.RootPath, newValue.Name);
            HeadList.Reset(repository.RootPath, newValue.Name, autoSelectFirst: true);
        }
        else
        {
            _commitCache.Invalidate(null, null);
            BaseList.Reset(string.Empty, string.Empty);
            HeadList.Reset(string.Empty, string.Empty);
        }

        // 记住每个仓库上次选中的分支（整体替换语义：先取出现状再改一个字段，ticket 03 的交接）。
        // 自动选中（加载时）同样流经这里，写回同样的值，无副作用。
        if (newValue is null || Repository is not { } openedRepository)
        {
            return;
        }

        var state = _settings.GetRepositoryState(openedRepository.RootPath);
        _settings.SaveRepositoryState(openedRepository.RootPath, state with { LastBranch = newValue.Name });
    }

    partial void OnFilterTextChanged(string? value) => ApplyFilter();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnInfoMessageChanged(string? value) => OnPropertyChanged(nameof(HasInfo));

    partial void OnIsBusyChanged(bool value)
    {
        FetchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FetchButtonText));
    }

    /// <summary>按过滤框内容把全量分支投影成分组列表；空组不出现（不过滤时保持完整两组）。</summary>
    private void ApplyFilter()
    {
        var filter = FilterText?.Trim();
        var visible = string.IsNullOrEmpty(filter)
            ? _allBranches
            : [.. _allBranches.Where(branch => branch.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];

        BranchGroups =
        [
            .. visible
                .GroupBy(branch => branch.IsRemote)
                .OrderBy(group => group.Key) // 本地组（false）在前，远程组在后
                .Select(group => new BranchGroup(group.Key ? "远程分支" : "本地分支", [.. group])),
        ];
    }

    /// <summary>
    /// 任一栏的选中项变化（用户点选）：更新详情预览为「最后点选的提交」——
    /// 详情卡是两栏共用的查看入口，显示的是用户最近关心（最近点击）的那个提交。
    /// </summary>
    private void OnColumnSelectionActed(object? sender, CommitItem? commit)
    {
        if (Repository is { } repository)
        {
            _ = Preview.LoadAsync(repository.RootPath, commit);
        }
    }

    /// <summary>栏的选中项变化时同步范围条（哈希 chip 与状态文案都来自两栏的选中状态）。</summary>
    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommitColumnViewModel.SelectedCommit))
        {
            OnPropertyChanged(nameof(HasRange));
            OnPropertyChanged(nameof(RangeBaseHash));
            OnPropertyChanged(nameof(RangeHeadHash));
            OnPropertyChanged(nameof(RangeText));
        }
    }
}

/// <summary>
/// 分支下拉中的一组（本地分支 / 远程分支）的显示模型：组标题 + 组内候选项。
/// </summary>
public sealed record BranchGroup(string Title, IReadOnlyList<BranchItem> Items);

/// <summary>
/// 分支候选项的显示模型：名称与选中态。游离头指针项显示为「（游离）」。
/// 候选的提交摘要（<see cref="GitHarvest.Core.Git.CommitSummary"/>）本页不展示——
/// 下拉只列分支名；提交摘要留给 ticket 07 的提交列表。
/// </summary>
public sealed partial class BranchItem : ObservableObject
{
    private BranchItem(BranchInfo branch)
    {
        Name = branch.Name;
        IsRemote = branch.IsRemote;
        IsCurrent = branch.IsCurrent;
        IsDetached = branch.IsDetached;
    }

    /// <summary>分支短名（本地 <c>main</c>、远程 <c>origin/main</c>；游离项为 "HEAD"）。</summary>
    public string Name { get; }

    /// <summary>显示名：游离头指针项标注「（游离）」。</summary>
    public string DisplayName => IsDetached ? "（游离）" : Name;

    public bool IsRemote { get; }

    public bool IsCurrent { get; }

    public bool IsDetached { get; }

    /// <summary>是否当前选中（驱动项内的 ✓ 标记）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    public static BranchItem FromBranchInfo(BranchInfo branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return new BranchItem(branch);
    }
}
