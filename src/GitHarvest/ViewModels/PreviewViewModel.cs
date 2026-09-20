using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
using Serilog;

namespace GitHarvest.ViewModels;

/// <summary>
/// 导出前总预览页（工作流第 3 步）的状态：祖先校验与双点差异经
/// <see cref="IExportService"/> 编排（不直接碰 git 进程），按变更类型分组展示文件
/// 清单与统计，支持类型过滤（统计卡与 chip 同一过滤态）、路径筛选、组展开收起与
/// 「重新计算」；输出路径在本页行内更改并按仓库记住。
/// 范围来自 <see cref="IRepositorySession.SelectedRange"/>（第 2 步写入）；
/// 未选定范围时显示引导卡。
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly IExportService _exportService;
    private readonly IRepositorySession _session;
    private readonly ISettingsService _settings;
    private readonly IFolderPicker _folderPicker;
    private readonly IShellNavigator _navigator;
    private readonly ILogger _logger;

    /// <summary>最近一次成功计算的汇总；过滤与统计都从它投影。</summary>
    private ChangeRangeSummary? _summary;

    public PreviewViewModel(
        ShellViewModel shell,
        IExportService exportService,
        IRepositorySession session,
        ISettingsService settings,
        IFolderPicker folderPicker,
        IShellNavigator navigator,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(exportService);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        _exportService = exportService;
        _session = session;
        _settings = settings;
        _folderPicker = folderPicker;
        _navigator = navigator;
        _logger = logger;

        Statistics = [.. GroupDefinitions.Select(def => new ChangeKindStat(def.Kind, def.Title))];
        KindFilterOptions =
        [
            new KindFilterOption(null, "全部"),
            .. GroupDefinitions.Select(def => new KindFilterOption(def.Kind, def.Title)),
        ];
    }

    /// <summary>五类变更的分组定义（标题、说明与徽章文案的唯一来源）。</summary>
    private static readonly IReadOnlyList<GroupDefinition> GroupDefinitions =
    [
        new(ChangeKind.Added, "新增", "仅出现在「更新后」"),
        new(ChangeKind.Deleted, "删除", "仅出现在「更新前」"),
        new(ChangeKind.Modified, "修改", "两侧均有快照"),
        new(ChangeKind.Renamed, "重命名", "按相似度 ≥ 50% 判定"),
        new(ChangeKind.Other, "其他", "类型变更 / 复制 / 未合并 / 子模块指针"),
    ];

    /// <summary>外壳状态（页面标题、步骤指示条等）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>会话里是否有选齐的变更范围（决定显示引导卡还是预览内容）。</summary>
    public bool HasRange => _session.SelectedRange is not null;

    /// <summary>范围条上基准一侧的短哈希；未选定范围时为「—」。</summary>
    public string RangeBaseHash => _session.SelectedRange?.Base.ShortHash ?? "—";

    /// <summary>范围条上 Head 一侧的短哈希；未选定范围时为「—」。</summary>
    public string RangeHeadHash => _session.SelectedRange?.Head.ShortHash ?? "—";

    /// <summary>副标题的文件数与说明段（原型 page-sub 的后半句）；计算中 / 失败时给出对应文案。</summary>
    public string RangeCountText
    {
        get
        {
            if (IsCalculating)
            {
                return "正在计算变更范围…";
            }

            if (_summary is { } summary)
            {
                return summary.IsEmpty
                    ? "范围内没有任何文件变更。"
                    : $"共 {summary.TotalCount} 个文件，按变更类型分组确认。";
            }

            return HasRange ? string.Empty : "选定基准提交与 Head 提交后，这里会按变更类型分组展示文件清单与统计。";
        }
    }

    /// <summary>五个统计卡（每类变更的数量与占比；点击即把类型过滤切到该类）。</summary>
    public IReadOnlyList<ChangeKindStat> Statistics { get; }

    /// <summary>类型过滤的 chip 候选（全部 + 五类），与统计卡共享同一选中态。</summary>
    public IReadOnlyList<KindFilterOption> KindFilterOptions { get; }

    /// <summary>当前类型过滤（<see langword="null"/> = 全部）。变化即重投影统计、chip 与分组。</summary>
    public ChangeKind? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (_selectedKind == value)
            {
                return;
            }

            _selectedKind = value;
            RebuildView();
        }
    }

    /// <summary><see cref="SelectedKind"/> 的后备字段。</summary>
    private ChangeKind? _selectedKind;

    /// <summary>按类型过滤与路径筛选后的分组（空组不出现，与原型 renderGroups 一致）。</summary>
    [ObservableProperty]
    private IReadOnlyList<PreviewGroupViewModel> _groups = [];

    /// <summary>文件路径筛选词（命中路径或重命名的旧路径；大小写不敏感）。</summary>
    [ObservableProperty]
    private string? _filterText;

    /// <summary>正在计算（祖先校验 + 双点差异）。</summary>
    [ObservableProperty]
    private bool _isCalculating;

    /// <summary>当前空闲；作为「重新计算」命令的 CanExecute 条件。</summary>
    public bool IsIdle => !IsCalculating;

    /// <summary>「重新计算」按钮文案：进行中给出反馈（与「拉取」按钮同一手法）。</summary>
    public string RecalculateButtonText => IsCalculating ? "计算中…" : "重新计算";

    /// <summary>计算或校验失败的提示（含祖先校验不通过的禁止导出解释）。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>是否显示错误横幅。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>是否已得到预览内容（成功结论：非空清单或空范围都算）——统计 / 过滤 / 输出路径条的可见性。</summary>
    [ObservableProperty]
    private bool _hasSummary;

    /// <summary>变更范围内没有任何文件变化（提示并禁止导出）。</summary>
    public bool IsEmptyRange => _summary is { IsEmpty: true };

    /// <summary>输出路径展示值：每仓库记住的上次输出路径优先，其次全局默认输出路径。</summary>
    [ObservableProperty]
    private string _outputPath = string.Empty;

    /// <summary>输出路径的展示文案：未设置时给出引导而不是空行。</summary>
    public string OutputPathDisplay => OutputPath.Length > 0
        ? OutputPath
        : "未设置，点击「更改…」选择输出目录";

    /// <summary>
    /// 页面加载：未打开仓库或未选定范围时停在引导卡；否则解析默认输出路径并自动计算
    /// （每次导航都会新建本页，Loaded 只执行一次，自动重算即「进入页面即最新」）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        OnPropertyChanged(nameof(HasRange));
        OnPropertyChanged(nameof(RangeBaseHash));
        OnPropertyChanged(nameof(RangeHeadHash));
        OnPropertyChanged(nameof(RangeCountText));

        ResolveOutputPath();

        if (HasRange)
        {
            await RecalculateAsync();
        }
    }

    /// <summary>
    /// 「重新计算」：祖先校验 → 双点差异 → 重新投影统计与分组。
    /// 祖先校验不通过（分叉提交对）或 git 失败时清空内容并给出横幅；
    /// 空范围保留统计（全 0）并显示禁止导出的警告。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RecalculateAsync()
    {
        if (_session.SelectedRange is not { } range || _session.OpenedRepository is not { } repository)
        {
            return;
        }

        ErrorMessage = null;
        IsCalculating = true;
        try
        {
            var result = await _exportService.BuildPreviewAsync(
                repository.RootPath,
                range.Base.ShortHash,
                range.Head.ShortHash).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                _summary = result.Summary;
                HasSummary = true;
                RebuildView();
            }
            else
            {
                // 失败（含分叉提交对）：旧清单不可信，全部撤下换横幅。
                _summary = null;
                HasSummary = false;
                Groups = [];
                ErrorMessage = result.FailureMessage;
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "计算变更范围的操作被取消。";
        }
        finally
        {
            IsCalculating = false;
        }
    }

    /// <summary>把类型过滤切到指定类（<see langword="null"/> = 全部）；统计卡与 chip 共用。</summary>
    [RelayCommand]
    private void SelectKind(ChangeKind? kind) => SelectedKind = kind;

    /// <summary>「更改…」：文件夹选择器行内更改本次导出目的地，并按仓库记住（用户故事 43）。</summary>
    [RelayCommand]
    private void ChangeOutputPath()
    {
        if (_session.OpenedRepository is not { } repository)
        {
            return;
        }

        var picked = _folderPicker.PickFolder(OutputPath.Length > 0 ? OutputPath : null);
        if (picked is null || picked == OutputPath)
        {
            return;
        }

        OutputPath = picked;
        OnPropertyChanged(nameof(OutputPathDisplay));

        // 每仓库状态（整体替换语义：先取出现状再改一个字段，ticket 03 的交接）。
        // 写入失败由设置服务降级为 Warning（内存值仍生效），不打断本页操作。
        var state = _settings.GetRepositoryState(repository.RootPath);
        _settings.SaveRepositoryState(repository.RootPath, state with { LastOutputPath = picked });
        _logger.Information("输出路径已更改并按仓库记住：{RepositoryPath} → {OutputPath}", repository.RootPath, picked);
    }

    /// <summary>全部分组展开。</summary>
    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = true;
        }
    }

    /// <summary>全部分组收起。</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = false;
        }
    }

    partial void OnIsCalculatingChanged(bool value)
    {
        RecalculateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecalculateButtonText));
        OnPropertyChanged(nameof(RangeCountText));
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnFilterTextChanged(string? value) => RebuildView();

    /// <summary>输出路径显示值变化时同步展示文案。</summary>
    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(OutputPathDisplay));

    /// <summary>「返回：选择提交」：未选定范围时引导回工作流第 2 步。</summary>
    [RelayCommand]
    private void GoToPickCommits() => _navigator.NavigateTo(ShellPage.PickCommits);

    /// <summary>解析本次的默认输出路径：每仓库上次输出路径优先，其次全局默认（spec 用户故事 43）。</summary>
    private void ResolveOutputPath()
    {
        if (_session.OpenedRepository is not { } repository)
        {
            OutputPath = string.Empty;
            return;
        }

        var lastPath = _settings.GetRepositoryState(repository.RootPath).LastOutputPath;
        OutputPath = firstNonEmpty(lastPath, _settings.Settings.DefaultOutputPath);

        static string firstNonEmpty(string first, string second)
            => first.Length > 0 ? first : second;
    }

    /// <summary>过滤条件变化：从汇总重新投影统计卡、chip 与分组（展开态按类型保留）。</summary>
    private void RebuildView()
    {
        var summary = _summary;
        var keyword = FilterText?.Trim();

        foreach (var stat in Statistics)
        {
            stat.Refresh(summary);
            stat.IsSelected = SelectedKind == stat.Kind;
        }

        foreach (var option in KindFilterOptions)
        {
            // 「全部」chip 的计数是总数；其余按类型计数。
            option.Count = option.Kind is { } kind ? (summary?.CountOf(kind) ?? 0) : (summary?.TotalCount ?? 0);
            option.IsSelected = SelectedKind == option.Kind;
        }

        var previousExpanded = Groups.ToDictionary(group => group.Kind, group => group.IsExpanded);

        Groups =
        [
            .. GroupDefinitions
                .Where(def => SelectedKind is null || SelectedKind == def.Kind)
                .Select(def => (Definition: def, Items: FilterItems(summary, def.Kind, keyword)))
                .Where(projected => projected.Items.Count > 0)
                .Select(projected => new PreviewGroupViewModel(
                    projected.Definition.Kind,
                    projected.Definition.Title,
                    projected.Definition.Description,
                    projected.Items,
                    previousExpanded.TryGetValue(projected.Definition.Kind, out var expanded) ? expanded : true)),
        ];

        OnPropertyChanged(nameof(RangeCountText));
        OnPropertyChanged(nameof(IsEmptyRange));
    }

    /// <summary>取某一类型的文件并按筛选词过滤（未得到汇总时为空清单）。</summary>
    private static IReadOnlyList<PreviewFileItem> FilterItems(ChangeRangeSummary? summary, ChangeKind kind, string? keyword)
    {
        if (summary is null)
        {
            return [];
        }

        return [.. summary.FilesOf(kind).Where(file => Matches(file, keyword)).Select(PreviewFileItem.FromChangedFile)];
    }

    /// <summary>文件是否命中当前过滤：类型匹配 + 路径 / 旧路径包含筛选词（大小写不敏感）。</summary>
    private static bool Matches(ChangedFile file, string? keyword)
        => keyword is null
            || keyword.Length == 0
            || file.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (file.OldPath?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>一组的静态信息：类型、标题与说明。</summary>
    private sealed record GroupDefinition(ChangeKind Kind, string Title, string Description);
}

/// <summary>
/// 统计卡的显示模型：类型、数量与占比；可选中（即当前类型过滤）。
/// 数字随汇总刷新（<see cref="Refresh"/>），避免整个列表因一次重算而整体重建。
/// </summary>
public sealed partial class ChangeKindStat : ObservableObject
{
    /// <summary>卡片显示的大数（数量）。</summary>
    [ObservableProperty]
    private int _count;

    /// <summary>占比文案（「占比 38%」；空范围时为「占比 0%」）。</summary>
    [ObservableProperty]
    private string _percentText = "占比 0%";

    /// <summary>是否是当前选中的过滤类型（统计卡的强调色描边）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    public ChangeKindStat(ChangeKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    /// <summary>变更类型（驱动色块与数字的类型色）。</summary>
    public ChangeKind Kind { get; }

    /// <summary>类型名（新增 / 删除 / …）。</summary>
    public string Title { get; }

    /// <summary>用最新汇总刷新数字与占比。</summary>
    public void Refresh(ChangeRangeSummary? summary)
    {
        Count = summary?.CountOf(Kind) ?? 0;
        PercentText = $"占比 {summary?.PercentOf(Kind) ?? 0}%";
    }
}

/// <summary>
/// 类型过滤 chip 的显示模型：<see cref="Kind"/> 为 <see langword="null"/> 表示「全部」。
/// </summary>
public sealed partial class KindFilterOption : ObservableObject
{
    /// <summary>chip 显示的文件数。</summary>
    [ObservableProperty]
    private int _count;

    /// <summary>是否选中（当前过滤）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    public KindFilterOption(ChangeKind? kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    /// <summary>过滤的类型；<see langword="null"/> 表示「全部」。</summary>
    public ChangeKind? Kind { get; }

    /// <summary>chip 文案（全部 / 新增 / …）。</summary>
    public string Title { get; }
}

/// <summary>
/// 预览清单里的一组（按变更类型）：组头（计数徽章 + 标题 + 说明）与组内文件行。
/// 可展开 / 收起，展开态在重新过滤后按类型保留。
/// </summary>
public sealed partial class PreviewGroupViewModel : ObservableObject
{
    public PreviewGroupViewModel(
        ChangeKind kind,
        string title,
        string description,
        IReadOnlyList<PreviewFileItem> items,
        bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(items);

        Kind = kind;
        Title = title;
        Description = description;
        Items = items;
        _isExpanded = isExpanded;
    }

    /// <summary>变更类型（驱动计数徽章的配色）。</summary>
    public ChangeKind Kind { get; }

    /// <summary>组标题（新增 / 删除 / …）。</summary>
    public string Title { get; }

    /// <summary>组说明（原型 .g-desc：本类变更的快照去向或判定规则）。</summary>
    public string Description { get; }

    /// <summary>组内文件行（已按当前过滤筛选）。</summary>
    public IReadOnlyList<PreviewFileItem> Items { get; }

    /// <summary>组内文件数（徽章数字）。</summary>
    public int Count => Items.Count;

    /// <summary>是否展开（组头点击切换）。</summary>
    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>
/// 文件行的显示模型：把 <see cref="ChangedFile"/> 转成界面直接可绑的文案——
/// 徽章文字、+行数 / -行数（或「相似度 / 二进制 / 其他原因」的补充说明）与大小。
/// </summary>
public sealed class PreviewFileItem
{
    private PreviewFileItem(ChangedFile file)
    {
        Path = file.Path;
        OldPath = file.OldPath;
        Kind = file.Kind;
        KindTitle = KindTitleOf(file.Kind);
        hasLineCounts = file.HasLineCounts;
        additions = file.Additions;
        deletions = file.Deletions;
        AdditionsText = file.Additions is { } addedLines ? $"+{addedLines}" : string.Empty;
        DeletionsText = file.Deletions is { } deletedLines ? $"-{deletedLines}" : string.Empty;
        DeltaNote = BuildDeltaNote(file);
        SizeText = FileSizeFormatter.Format(file.SizeBytes);
    }

    /// <summary>git numstat 是否给出了行数（二进制文件没有）。</summary>
    private readonly bool hasLineCounts;

    /// <summary>新增 / 删除行数（没有行数时为 <see langword="null"/>）。</summary>
    private readonly int? additions;
    private readonly int? deletions;

    /// <summary>五类变更的徽章文字（与分组标题、更新说明同一来源，见 <see cref="ChangeKindTitles"/>）。</summary>
    private static string KindTitleOf(ChangeKind kind) => ChangeKindTitles.For(kind);

    /// <summary>「其他」类的具体原因文案（spec 用户故事 28：在清单中可见；来源同上）。</summary>
    private static string OtherReasonTitleOf(OtherChangeReason reason) => ChangeKindTitles.For(reason);

    /// <summary>当前路径（仓库相对，重命名 / 复制为新路径）。</summary>
    public string Path { get; }

    /// <summary>旧路径；仅重命名 / 复制非空。</summary>
    public string? OldPath { get; }

    /// <summary>是否重命名 / 复制（行内显示「旧 → 新」布局）。</summary>
    public bool IsRenamed => OldPath is not null;

    /// <summary>变更类型（驱动徽章配色）。</summary>
    public ChangeKind Kind { get; }

    /// <summary>徽章文字（新增 / 删除 / 修改 / 重命名 / 其他）。</summary>
    public string KindTitle { get; }

    /// <summary>
    /// 是否显示 +行数：只有新增 / 删除 / 修改行显示「+N -M」；
    /// 重命名行显示相似度、「其他」行显示原因（与原型行内 delta 位一致）、二进制没有行数。
    /// </summary>
    public bool ShowLineCounts =>
        hasLineCounts && Kind is ChangeKind.Added or ChangeKind.Deleted or ChangeKind.Modified;

    /// <summary>是否显示「+N」（行数位显示中且新增行数非 0——纯删除不显示绿色的 +0）。</summary>
    public bool ShowAdditions => ShowLineCounts && additions > 0;

    /// <summary>是否显示「-N」（行数位显示中且删除行数非 0——纯新增不显示红色的 -0）。</summary>
    public bool ShowDeletions => ShowLineCounts && deletions > 0;

    /// <summary>是否显示补充说明（相似度 / 二进制 / 其他原因）。</summary>
    public bool ShowDeltaNote => !ShowLineCounts && DeltaNote.Length > 0;

    /// <summary>新增行数文案（「+86」）。</summary>
    public string AdditionsText { get; }

    /// <summary>删除行数文案（「-23」）。</summary>
    public string DeletionsText { get; }

    /// <summary>
    /// 无行数时的补充说明：重命名显示相似度、二进制显示「二进制」、
    /// 「其他」显示具体原因（类型变更 / 复制 / 未合并 / 子模块指针）。
    /// </summary>
    public string DeltaNote { get; }

    /// <summary>展示大小（「8.2 KB」；子模块指针为「—」）。</summary>
    public string SizeText { get; }

    /// <summary>组装行数位之外的说明文案（重命名相似度 / 二进制 / 其他原因）。
    /// 二进制的判据是增删两侧都缺（git numstat 对二进制两侧都以 "-" 占位）——
    /// 只缺一侧（如纯删除文件的新增行为 0）不是二进制，不能误标。</summary>
    private static string BuildDeltaNote(ChangedFile file) => file.Kind switch
    {
        ChangeKind.Renamed => file.SimilarityPercent is { } similarity ? $"相似度 {similarity}%" : string.Empty,
        ChangeKind.Other => OtherReasonTitleOf(file.OtherReason),
        _ when file.Additions is null && file.Deletions is null => "二进制",
        _ => string.Empty,
    };

    public static PreviewFileItem FromChangedFile(ChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new PreviewFileItem(file);
    }
}
