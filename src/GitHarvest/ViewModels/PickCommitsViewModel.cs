using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Export;
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
/// 只依赖 Core 接口——分支、提交与拉取走 <see cref="IGitService"/>，选齐后的即时祖先校验
/// 与文件数走 <see cref="IExportService"/>，上次分支走 <see cref="ISettingsService"/>，
/// 当前仓库来自 <see cref="IRepositorySession"/>。
/// </summary>
public sealed partial class PickCommitsViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settings;
    private readonly IRepositorySession _session;
    private readonly IShellNavigator _navigator;

    /// <summary>两栏共享的提交搜索缓存（首次搜索后台整份读一次，之后过滤与翻页零 git 调用）。</summary>
    private readonly CommitSearchCache _commitCache;

    /// <summary>全量分支候选项（过滤前的源），过滤投影到 <see cref="BranchGroups"/>。</summary>
    private IReadOnlyList<BranchItem> _allBranches = [];

    /// <summary>祖先校验的竞态令牌：两栏任一选中变化即自增，慢的旧校验结果晚归直接丢弃
    /// （与提交列表查询的 <c>_generation</c> 同一套路）。</summary>
    private int _validationGeneration;

    public PickCommitsViewModel(
        ShellViewModel shell,
        IGitService gitService,
        IExportService exportService,
        ISettingsService settings,
        IRepositorySession session,
        IShellNavigator navigator,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(exportService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        _gitService = gitService;
        _exportService = exportService;
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

    /// <summary>
    /// 范围条的状态文案：未选齐给引导、选齐后随祖先校验的结论更新
    /// （校验中 / 通过带提交数 / 分叉 / 流程失败），对应原型 range-bar 的 .count。
    /// </summary>
    [ObservableProperty]
    private string _rangeStatText = "请分别在两栏指定基准提交与 Head 提交。";

    /// <summary>祖先校验横幅的状态：未选齐（不显示）/ 通过（绿）/ 流程失败（中性黄）/ 分叉（红）。</summary>
    [ObservableProperty]
    private AncestryBannerKind _ancestryState = AncestryBannerKind.None;

    /// <summary>中性黄横幅的失败原因（校验流程本身失败时给出，如分支被删、git 不可用）。</summary>
    [ObservableProperty]
    private string? _ancestryNote;

    /// <summary>通过横幅与范围条统计里的加粗提交数（「共经过 N 次提交」；基准与 Head 同一提交时为 0）。</summary>
    [ObservableProperty]
    private string _rangeCommitCountText = "0";

    /// <summary>范围条统计里的加粗文件数（「变更范围：N 个文件」，来自导出编排的汇总——
    /// 与第 3 步预览页的「共 N 个文件」同源）。</summary>
    [ObservableProperty]
    private string _rangeFilesText = "0";

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
    /// 加载结束后跑一次范围门控：空分支 / 读取失败时不会有任何选中事件，
    /// 显式评估一次保证「未选齐 → 下一步置灰」在页面打开时即生效。
    /// </summary>
    [RelayCommand]
    private async Task LoadBranchesAsync()
    {
        if (Repository is not { } repository)
        {
            // 未打开仓库：没有可选内容，状态与门控统一交给范围校验收敛（未选齐 → 下一步置灰）。
            await ValidateRangeAsync();
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

        await ValidateRangeAsync();
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

    /// <summary>栏的选中项变化时同步范围条、仓库会话与即时祖先校验（原型 validate() 的触发点：
    /// 任一栏选中变化立即校验一次，横幅 / 统计 / 「下一步」门控同源）。
    /// 会话里的 <see cref="IRepositorySession.SelectedRange"/> 是第 3 步「导出前总预览」的数据入口——
    /// 基准与 Head 选齐即整体写入，任一侧被取消（含切分支时的重置）即整体清空。</summary>
    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommitColumnViewModel.SelectedCommit))
        {
            OnPropertyChanged(nameof(HasRange));
            OnPropertyChanged(nameof(RangeBaseHash));
            OnPropertyChanged(nameof(RangeHeadHash));
            UpdateSessionRange();
            _ = ValidateRangeAsync();
        }
    }

    /// <summary>
    /// 即时祖先校验（补遗：原型把校验画在第 2 步，ticket 08 的校验在第 3 步）：
    /// 未选齐先清态，且按所有者决策「下一步」置灰——基准或 Head 任一未选都无法进入第 3 步；
    /// 选齐后经 <see cref="IExportService.BuildPreviewAsync"/>
    /// 一次拿到祖先结论与文件数，再补提交数——横幅、范围条统计与「下一步」门控三处输出同源。
    /// 快速改选用代际令牌丢弃过期结果。流程失败（分支被删等）显示中性提示，
    /// 不显示「通过」；此时范围已选齐，不额外拦截（拦的只是确定结论：未选齐与分叉）。
    /// </summary>
    private async Task ValidateRangeAsync()
    {
        var generation = ++_validationGeneration;

        // 未选齐（或仓库已不在）：横幅不出现，统计回到引导文案；「下一步」置灰
        // （所有者决策：基准或 Head 任一未选都进不了第 3 步；侧边栏自由导航不受影响）。
        if (!HasRange || Repository is null)
        {
            AncestryState = AncestryBannerKind.None;
            AncestryNote = null;
            RangeStatText = "请分别在两栏指定基准提交与 Head 提交。";
            Shell.PageBlocksNext = true;
            return;
        }

        RangeStatText = "正在校验祖先关系…";
        var baseHash = BaseList.SelectedCommit!.ShortHash;
        var headHash = HeadList.SelectedCommit!.ShortHash;

        // 导出编排的第一步：祖先校验 → 双点差异 → 汇总，与第 3 步预览同源（文件数由此而来）。
        var preview = await _exportService.BuildPreviewAsync(Repository.RootPath, baseHash, headHash)
            .ConfigureAwait(true);
        if (generation != _validationGeneration)
        {
            return;
        }

        if (preview.IsAncestryViolated)
        {
            AncestryState = AncestryBannerKind.Error;
            AncestryNote = null;
            RangeStatText = "基准与 Head 不在同一祖先链上，无法导出。";
            Shell.PageBlocksNext = true;
            return;
        }

        if (!preview.IsSuccess)
        {
            AncestryState = AncestryBannerKind.Warn;
            AncestryNote = preview.FailureMessage;
            RangeStatText = "祖先校验未完成。";
            Shell.PageBlocksNext = false;
            return;
        }

        var count = await _gitService.GetRangeCommitCountAsync(Repository.RootPath, baseHash, headHash)
            .ConfigureAwait(true);
        if (generation != _validationGeneration)
        {
            return;
        }

        if (!count.IsSuccess)
        {
            // 祖先与文件数都拿到了却拿不到提交数：按中性提示处理，不显示「通过」误导用户。
            AncestryState = AncestryBannerKind.Warn;
            AncestryNote = count.FailureMessage;
            RangeStatText = "祖先校验未完成。";
            Shell.PageBlocksNext = false;
            return;
        }

        AncestryState = AncestryBannerKind.Ok;
        AncestryNote = null;
        RangeFilesText = preview.Summary!.TotalCount.ToString(CultureInfo.InvariantCulture);
        RangeCommitCountText = count.Count!.Value.ToString(CultureInfo.InvariantCulture);
        // 整句在 Ok 态并不显示：页脚用分段元素渲染统计（数字单独加粗），此处仍按同一文案赋值，
        // 使「整句」与「分段」两处口径无论哪处被渲染都是同一句话，不会日后各改一半。
        RangeStatText = $"变更范围：{preview.Summary.TotalCount} 个文件 · {count.Count} 次提交";
        Shell.PageBlocksNext = false;
    }

    /// <summary>把两栏的选中提交写入会话（选齐才写，未选齐清空）；摘要在 Core 与界面间复用同一形态。</summary>
    private void UpdateSessionRange()
        => _session.SelectedRange = HasRange
            ? new RangeSelection(
                ToSummary(BaseList.SelectedCommit!),
                ToSummary(HeadList.SelectedCommit!))
            : null;

    private static CommitSummary ToSummary(CommitItem item)
        => new(item.ShortHash, item.Subject, item.AuthorName, item.AuthorTime);
}

/// <summary>
/// 分支下拉中的一组（本地分支 / 远程分支）的显示模型：组标题 + 组内候选项。
/// </summary>
public sealed record BranchGroup(string Title, IReadOnlyList<BranchItem> Items);

/// <summary>
/// 第 2 步即时祖先校验横幅的状态（对应原型 page-pick 的 validBanner）：
/// 未选齐不显示；流程失败（校验本身没跑成）用中性黄提示而不是「通过 / 失败」结论。
/// </summary>
public enum AncestryBannerKind
{
    /// <summary>未选齐基准与 Head：不显示横幅。</summary>
    None,

    /// <summary>校验通过（绿）：基准是 Head 的祖先，横幅带提交数。</summary>
    Ok,

    /// <summary>校验流程失败（中性黄）：分支被删、git 不可用等，给失败原因，不给「通过 / 失败」结论。</summary>
    Warn,

    /// <summary>校验结论为分叉提交对（红）：基准不是 Head 的祖先，禁止导出并提示调整。</summary>
    Error,
}

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
