using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Markdown;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
using GitHarvest.Core.Templates;
using Serilog;

namespace GitHarvest.ViewModels;

/// <summary>更新说明页（工作流第 4 步）的页面状态。</summary>
public enum NotesPageState
{
    /// <summary>未打开仓库或未选定范围：显示引导卡，不能导出。</summary>
    Guide,

    /// <summary>范围就绪、检查通过：可以导出。</summary>
    Ready,

    /// <summary>导出前检查发现文件系统冲突：列出清单并禁止导出（用户故事 32）。</summary>
    ConflictBlocked,

    /// <summary>正在导出（进度条 + 取消按钮）。</summary>
    Exporting,

    /// <summary>导出完成（展示产物位置并可打开输出文件夹）。</summary>
    Completed,

    /// <summary>导出失败（Git 或文件系统问题，半成品已清理）。</summary>
    Failed,

    /// <summary>用户取消了导出（半成品已清理）。</summary>
    Cancelled,
}

/// <summary>
/// 更新说明页（工作流第 4 步）的状态：导出链路（ticket 09）+ 说明编辑（ticket 10）——
/// 双栏编辑器（左 Markdown / 右实时预览）、工具栏插入占位符与格式、「重置为模板」；
/// 说明草稿按当前范围经 <see cref="ITemplateService"/> 渲染生成，编辑内容存进会话
/// （只影响本次导出、不动模板本身），导出时随请求交给 Core 渲染写出。
/// 范围来自 <see cref="IRepositorySession.SelectedRange"/>（第 2 步写入）；
/// 导出本身经 <see cref="IExportService"/>（本类不碰 git 进程与文件系统）。
/// </summary>
public sealed partial class NotesViewModel : ObservableObject
{
    private readonly IExportService _exportService;
    private readonly ITemplateService _templateService;
    private readonly IRepositorySession _session;
    private readonly ISettingsService _settings;
    private readonly IExportWarningGate _warningGate;
    private readonly IFolderOpener _folderOpener;
    private readonly IShellNavigator _navigator;
    private readonly ILogger _logger;

    /// <summary>当前导出的取消源；没有导出在跑时为 <see langword="null"/>。</summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>最近一次成功导出的更新包目录（供「打开输出文件夹」用）。</summary>
    private string? _outputPackagePath;

    /// <summary>预检带出的变更范围汇总：说明草稿与实时预览的占位符取值都从它算（与导出同一份数字）。</summary>
    private ChangeRangeSummary? _summary;

    /// <summary>进入页面的时间点：默认目录名与导出请求的时间戳都取它，避免跨零点时两处不一致。</summary>
    private readonly DateTimeOffset _openedAt = DateTimeOffset.Now;

    public NotesViewModel(
        ShellViewModel shell,
        IExportService exportService,
        ITemplateService templateService,
        IRepositorySession session,
        ISettingsService settings,
        IExportWarningGate warningGate,
        IFolderOpener folderOpener,
        IShellNavigator navigator,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(exportService);
        ArgumentNullException.ThrowIfNull(templateService);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(warningGate);
        ArgumentNullException.ThrowIfNull(folderOpener);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        _exportService = exportService;
        _templateService = templateService;
        _session = session;
        _settings = settings;
        _warningGate = warningGate;
        _folderOpener = folderOpener;
        _navigator = navigator;
        _logger = logger;

        // 默认的更新日期目录名（用户故事 31）；页面每次进入都重建，因此总是「今天」。
        FolderName = ExportFolderName.DefaultFor(_openedAt);
    }

    /// <summary>外壳状态（页面标题、步骤指示条、页脚步进按钮）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>页面状态；变化时刷新下方所有由它派生的可见性与文案。</summary>
    [ObservableProperty]
    private NotesPageState _state = NotesPageState.Guide;

    /// <summary>会话里是否选齐了变更范围（决定显示引导卡还是导出内容）。</summary>
    public bool HasRange => _session.SelectedRange is not null;

    /// <summary>范围条上的双点短哈希（「a1b2c3d..e4f5g6h」）；未选齐时为「—」。</summary>
    public string RangeText => _session.SelectedRange?.RangeText ?? "—";

    /// <summary>输出路径（「本次 &gt; 每仓库上次 &gt; 全局默认」，用户故事 43）；本页只读展示。</summary>
    [ObservableProperty]
    private string _outputPath = string.Empty;

    /// <summary>输出路径的展示文案：未配置时给出引导而不是空行。</summary>
    public string OutputPathDisplay => OutputPath.Length > 0
        ? OutputPath
        : "未设置，请回到「导出前总预览」页选择输出目录";

    /// <summary>更新日期目录名（可编辑，默认 yyyy-MM-dd）。</summary>
    [ObservableProperty]
    private string _folderName = string.Empty;

    /// <summary>目录名的校验提示；合法时为空。</summary>
    public string FolderNameError => ExportFolderName.Validate(FolderName) ?? string.Empty;

    /// <summary>目录名是否合法（驱动导出按钮的可用性与输入框的错误态）。</summary>
    public bool HasFolderNameError => FolderNameError.Length > 0;

    /// <summary>是否显示「输出路径落在仓库工作区内」的一次性警告条（用户故事 33）。</summary>
    [ObservableProperty]
    private bool _needsInsideRepositoryWarning;

    /// <summary>「更新前」文件夹将包含的文件数。</summary>
    [ObservableProperty]
    private int _beforeFileCount;

    /// <summary>「更新后」文件夹将包含的文件数。</summary>
    [ObservableProperty]
    private int _afterFileCount;

    /// <summary>新增文件数（更新包结构说明用）。</summary>
    [ObservableProperty]
    private int _addedFileCount;

    /// <summary>删除文件数（更新包结构说明用）。</summary>
    [ObservableProperty]
    private int _deletedFileCount;

    /// <summary>「更新前」一行的补充说明（原型：「（含 N 个已删除文件）」）；没有删除时为假。</summary>
    public string DeletedFileNote => DeletedFileCount > 0 ? $"（含 {DeletedFileCount} 个已删除文件）" : string.Empty;

    /// <summary>「更新后」一行的补充说明（原型：「（含 N 个新增文件）」）；没有新增时为假。</summary>
    public string AddedFileNote => AddedFileCount > 0 ? $"（含 {AddedFileCount} 个新增文件）" : string.Empty;

    /// <summary>是否已算出更新包结构（有范围且算过一遍：待导出 / 导出中 / 已完成都显示）。</summary>
    [ObservableProperty]
    private bool _hasStructure;

    /// <summary>冲突清单（导出前检查的结果，用户故事 32）。</summary>
    [ObservableProperty]
    private IReadOnlyList<FileSystemConflict> _conflicts = [];

    /// <summary>状态卡的主标题。</summary>
    [ObservableProperty]
    private string _statusTitle = string.Empty;

    /// <summary>状态卡的副文案（进度阶段、产物位置、失败原因）。</summary>
    [ObservableProperty]
    private string _statusSubtitle = string.Empty;

    /// <summary>状态卡的 chip 文案（准备就绪 / 进行中 / 成功 / 失败 / 已取消）。</summary>
    [ObservableProperty]
    private string _statusChip = string.Empty;

    /// <summary>导出进度百分比（0–100）。</summary>
    [ObservableProperty]
    private int _progressPercent;

    /// <summary>导出中（进度条可见、取消按钮可用、开始导出禁用）。</summary>
    public bool IsExporting => State == NotesPageState.Exporting;

    /// <summary>显示引导卡（未打开仓库或未选定范围）。</summary>
    public bool ShowGuide => State == NotesPageState.Guide;

    /// <summary>显示冲突清单卡。</summary>
    public bool ShowConflicts => State == NotesPageState.ConflictBlocked;

    /// <summary>显示状态卡（除引导与冲突外的所有状态）。</summary>
    public bool ShowStatus => State is not (NotesPageState.Guide or NotesPageState.ConflictBlocked);

    /// <summary>显示「打开输出文件夹」（仅在导出成功后）。</summary>
    public bool ShowOpenOutputFolder => State == NotesPageState.Completed;

    /// <summary>显示完成提示浮层（导出成功时出现一次，用户关掉即消失）。</summary>
    [ObservableProperty]
    private bool _showCompletionPrompt;

    /// <summary>工具栏「插入占位符」菜单的数据源（12 个占位符目录，Core 的唯一真相源）。</summary>
    public IReadOnlyList<TemplatePlaceholder> Placeholders => NotesTemplateCatalog.Placeholders;

    /// <summary>工具栏右侧的占位符说明：个数取自 Core 目录（不在界面层再写死一份）。</summary>
    public string PlaceholderToolbarHint
        => $"共 {NotesTemplateCatalog.Placeholders.Count} 个模板占位符 · 默认模板可在全局设置中指定";

    /// <summary>
    /// 编辑器内容（左栏 Markdown 原文，可含占位符）：初始为按当前范围渲染出的说明草稿，
    /// 编辑逐键写回会话草稿（页面来回切换不丢），导出时作为 <see cref="ExportRequest.NotesOverride"/> 交给 Core。
    /// </summary>
    [ObservableProperty]
    private string _editorText = string.Empty;

    /// <summary>右栏实时预览的块序列（占位符先渲染、再过迷你解析；所见即所得）。</summary>
    [ObservableProperty]
    private IReadOnlyList<MarkdownBlock> _previewBlocks = [];

    /// <summary>自定义模板读取失败的回退提示（用户故事 38）；未走回退时为空。</summary>
    [ObservableProperty]
    private string _templateNotice = string.Empty;

    /// <summary>是否显示模板回退提示条。</summary>
    public bool HasTemplateNotice => TemplateNotice.Length > 0;

    /// <summary>未知占位符的合并警告（用户故事 39：原样保留并提示笔误）；没有时为空。</summary>
    [ObservableProperty]
    private string _placeholderWarning = string.Empty;

    /// <summary>是否显示未知占位符警告条。</summary>
    public bool HasPlaceholderWarning => PlaceholderWarning.Length > 0;

    /// <summary>是否显示编辑器卡（范围与汇总就绪——引导、空范围、预检失败时不显示）。</summary>
    public bool ShowEditor => _summary is not null;

    /// <summary>编辑器是否可编辑（导出进行中锁定，防止内容与写出中的说明打架）。</summary>
    public bool CanEdit => _summary is not null && !IsExporting;

    /// <summary>是否可以开始导出（就绪、目录名合法、当前没有导出在跑）。</summary>
    public bool CanStartExport => State == NotesPageState.Ready && !HasFolderNameError;

    /// <summary>状态 chip 的语义（驱动配色）：成功 / 失败 / 进行中 / 中性。</summary>
    public string StatusKind => State switch
    {
        NotesPageState.Completed => "Success",
        NotesPageState.Failed or NotesPageState.ConflictBlocked => "Error",
        NotesPageState.Exporting => "Progress",
        _ => "Neutral",
    };

    /// <summary>
    /// 页面加载：解析输出路径与范围，再经 Core 的预检一次拿到更新包结构与冲突清单
    /// （文件系统冲突在按下导出前就告诉用户，用户故事 32）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        NotifyRangeChanged();
        ResolveOutputPath();
        ResetToReadyState();

        // 本页是工作流最后一步：页脚的「开始导出」按钮由页面接管（壳按 CanGoNext 置灰）。
        Shell.NextAction = StartExportCommand;

        if (_session.SelectedRange is null || _session.OpenedRepository is null)
        {
            State = NotesPageState.Guide;
            HasStructure = false;
            ResetEditor();
            return;
        }

        var precheck = await _exportService.InspectAsync(CreateRequest()).ConfigureAwait(true);

        if (precheck.IsFailed)
        {
            // 输出路径还没选、或 git 访问失败：本页给出原因并禁止导出（第 3 步另有解释）。
            HasStructure = false;
            ResetEditor();
            ShowUnavailable(precheck.FailureMessage ?? "无法导出。");
            return;
        }

        if (precheck.IsEmptyRange)
        {
            HasStructure = false;
            ResetEditor();
            ShowUnavailable(precheck.FailureMessage ?? "变更范围内没有任何文件变更。");
            return;
        }

        ApplyPlan(precheck.Plan!);
        await SetupEditorAsync(precheck.Summary).ConfigureAwait(true);

        if (precheck.IsBlockedByConflicts)
        {
            Conflicts = precheck.Conflicts!;
            State = NotesPageState.ConflictBlocked;
            return;
        }

        State = NotesPageState.Ready;
    }

    /// <summary>
    /// 「开始导出」。输出路径落在仓库工作区内时先给一次性警告（用户故事 33），
    /// 用户确认后才真正导出；已确认过（同一会话）则直接导出。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartExport))]
    private async Task StartExportAsync()
    {
        if (_session.OpenedRepository is not { } repository || _session.SelectedRange is null)
        {
            return;
        }

        // 输出路径落在仓库工作区内会污染 git status：提示一次、用户可继续（bare 仓库没有工作区，跳过）。
        if (!repository.IsBare
            && OutputPathPlacement.IsInsideRepository(OutputPath, repository.RootPath)
            && _warningGate.ShouldWarnOutputInsideRepository)
        {
            NeedsInsideRepositoryWarning = true;
            return;
        }

        await RunExportAsync().ConfigureAwait(true);
    }

    /// <summary>用户在一次性警告里选择「仍然导出」：记下确认（本会话不再询问）并开始导出。</summary>
    [RelayCommand]
    private async Task ContinueAfterWarningAsync()
    {
        _warningGate.AcknowledgeOutputInsideRepository();
        NeedsInsideRepositoryWarning = false;

        await RunExportAsync().ConfigureAwait(true);
    }

    /// <summary>用户在一次性警告里选择「取消」：收起警告，不导出。</summary>
    [RelayCommand]
    private void CancelWarning() => NeedsInsideRepositoryWarning = false;

    /// <summary>取消正在进行的导出：Core 会中断 git 进程并清理半成品目录。</summary>
    [RelayCommand]
    private void CancelExport() => _cancellation?.Cancel();

    /// <summary>关闭完成提示浮层（产物位置仍在状态卡里可查）。</summary>
    [RelayCommand]
    private void DismissCompletionPrompt() => ShowCompletionPrompt = false;

    /// <summary>在系统文件管理器里打开更新包目录（用户故事 35）。</summary>
    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (_outputPackagePath is not { } path)
        {
            return;
        }

        if (!_folderOpener.Open(path))
        {
            StatusSubtitle = $"无法打开输出目录，请手动前往：{path}";
            _logger.Warning("打开输出目录失败：{PackagePath}", path);
        }
    }

    /// <summary>未打开仓库或未选定范围时，引导回工作流第 2 步。</summary>
    [RelayCommand]
    private void GoToPickCommits() => _navigator.NavigateTo(ShellPage.PickCommits);

    /// <summary>
    /// 「重置为模板」：丢弃当前编辑内容，按当前范围重新渲染模板生成草稿（用户故事 40）。
    /// 模板经 <see cref="ITemplateService.LoadTemplateAsync"/> 重新加载——设置里的自定义模板刚换过也立即生效。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ResetToTemplateAsync()
    {
        if (_summary is null)
        {
            return;
        }

        EditorText = await GenerateDraftAsync().ConfigureAwait(true);
    }

    partial void OnStateChanged(NotesPageState value)
    {
        OnPropertyChanged(nameof(IsExporting));
        OnPropertyChanged(nameof(ShowGuide));
        OnPropertyChanged(nameof(ShowConflicts));
        OnPropertyChanged(nameof(ShowStatus));
        OnPropertyChanged(nameof(ShowOpenOutputFolder));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(CanEdit));
        StartExportCommand.NotifyCanExecuteChanged();
        ResetToTemplateCommand.NotifyCanExecuteChanged();
    }

    partial void OnFolderNameChanged(string value)
    {
        OnPropertyChanged(nameof(FolderNameError));
        OnPropertyChanged(nameof(HasFolderNameError));
        OnPropertyChanged(nameof(CanStartExport));
        StartExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(OutputPathDisplay));

    partial void OnEditorTextChanged(string value)
    {
        // 草稿只跟着有范围的页面走：引导态清空编辑器时不写会话（那会儿的空串不是用户的选择）。
        if (HasRange)
        {
            _session.NotesDraft = value;
        }

        RefreshPreview();
    }

    partial void OnTemplateNoticeChanged(string value) => OnPropertyChanged(nameof(HasTemplateNotice));

    partial void OnPlaceholderWarningChanged(string value) => OnPropertyChanged(nameof(HasPlaceholderWarning));

    /// <summary>
    /// 预检带出了变更范围汇总后装配编辑器：有会话草稿先恢复（页面来回切换不丢编辑），
    /// 否则按当前范围渲染模板生成草稿。
    /// </summary>
    private async Task SetupEditorAsync(ChangeRangeSummary? summary)
    {
        _summary = summary;
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(CanEdit));
        ResetToTemplateCommand.NotifyCanExecuteChanged();

        if (summary is null)
        {
            EditorText = string.Empty;
            return;
        }

        EditorText = _session.NotesDraft ?? await GenerateDraftAsync().ConfigureAwait(true);
    }

    /// <summary>汇总不可用时的编辑器复位（引导 / 空范围 / 预检失败）。</summary>
    private void ResetEditor()
    {
        _summary = null;
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(CanEdit));
        ResetToTemplateCommand.NotifyCanExecuteChanged();
        EditorText = string.Empty;
        PreviewBlocks = [];
        TemplateNotice = string.Empty;
        PlaceholderWarning = string.Empty;
    }

    /// <summary>加载当前生效的模板并渲染成说明草稿；回退提示同时落到提示条上。</summary>
    private async Task<string> GenerateDraftAsync()
    {
        var loaded = await _templateService.LoadTemplateAsync().ConfigureAwait(true);
        TemplateNotice = loaded.Notice ?? string.Empty;

        return _templateService.Render(loaded.Text, CreateNotesContext()).Content;
    }

    /// <summary>
    /// 实时预览：编辑内容先过占位符渲染（未知占位符的警告同步刷新），再喂给迷你解析器。
    /// 与最终写出走同一个渲染入口（Core 的 <see cref="ITemplateService.Render"/>），所见即所得。
    /// </summary>
    private void RefreshPreview()
    {
        if (_summary is null)
        {
            PreviewBlocks = [];
            PlaceholderWarning = string.Empty;
            return;
        }

        var rendered = _templateService.Render(EditorText, CreateNotesContext());
        PlaceholderWarning = rendered.Warnings.Count > 0
            ? string.Join(' ', rendered.Warnings)
            : string.Empty;
        PreviewBlocks = MarkdownMiniParser.Parse(rendered.Content);
    }

    /// <summary>按当前范围与页面输入构建占位符上下文（与导出写出口径一致：同一份请求与汇总）。</summary>
    private NotesTemplateContext CreateNotesContext()
        => NotesTemplateContext.From(CreateRequest(), _summary!);

    partial void OnAddedFileCountChanged(int value) => OnPropertyChanged(nameof(AddedFileNote));

    partial void OnDeletedFileCountChanged(int value) => OnPropertyChanged(nameof(DeletedFileNote));

    /// <summary>执行一次导出：后台线程 + 进度回调，结束（含取消）后按结论落到对应状态。</summary>
    private async Task RunExportAsync()
    {
        if (_session.OpenedRepository is null || _session.SelectedRange is null)
        {
            return;
        }

        if (ExportFolderName.Validate(FolderName) is { } folderError)
        {
            ShowUnavailable(folderError);
            return;
        }

        var request = CreateRequest();

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        State = NotesPageState.Exporting;
        StatusTitle = "正在导出更新包…";
        StatusSubtitle = "正在准备导出…";
        StatusChip = "进行中";
        ProgressPercent = 0;

        // 进度回调在界面线程上重建（Progress<T> 捕获当前同步上下文），
        // 导出整体交给线程池：用户故事 34 要求后台执行、界面不卡。
        var progress = new Progress<ExportProgress>(OnExportProgress);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await Task
                .Run(() => _exportService.ExportAsync(request, progress, cancellation.Token), cancellation.Token)
                .ConfigureAwait(true);

            stopwatch.Stop();
            ApplyResult(result, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            State = NotesPageState.Cancelled;
            StatusTitle = "已取消导出";
            StatusSubtitle = "未完成的更新包已清理，输出路径下没有留下半成品。";
            StatusChip = "已取消";
            ProgressPercent = 0;
        }
        finally
        {
            _cancellation = null;
        }
    }

    /// <summary>把 Core 的四种导出结论落到页面状态上。</summary>
    private void ApplyResult(ExportResult result, TimeSpan elapsed)
    {
        switch (result.Outcome)
        {
            case ExportOutcome.Completed:
                _outputPackagePath = result.PackagePath;
                BeforeFileCount = result.BeforeFileCount;
                AfterFileCount = result.AfterFileCount;
                AddedFileCount = result.AddedFileCount;
                DeletedFileCount = result.DeletedFileCount;
                HasStructure = true;
                State = NotesPageState.Completed;
                StatusTitle = "更新包导出完成";
                StatusSubtitle = $"输出至 {result.PackagePath} · 耗时 {FormatElapsed(elapsed)} · 共 {FileSizeFormatter.Format(result.TotalBytes)}";
                StatusChip = "成功";
                ShowCompletionPrompt = true;
                ProgressPercent = 100;

                _logger.Information(
                    "导出完成：{PackagePath}（耗时 {Elapsed}，共 {TotalBytes} 字节）",
                    result.PackagePath,
                    elapsed,
                    result.TotalBytes);

                RememberOutputPath();
                break;

            case ExportOutcome.ConflictBlocked:
                Conflicts = result.Conflicts ?? [];
                State = NotesPageState.ConflictBlocked;
                _logger.Warning("导出被中止：发现 {ConflictCount} 处文件系统冲突。", Conflicts.Count);
                break;

            case ExportOutcome.EmptyRange:
                State = NotesPageState.Failed;
                StatusTitle = "没有可导出的内容";
                StatusSubtitle = result.FailureMessage ?? "变更范围内没有任何文件变更。";
                StatusChip = "不可导出";
                break;

            default:
                State = NotesPageState.Failed;
                StatusTitle = "导出失败";
                StatusSubtitle = result.FailureMessage ?? "导出失败，详情请查看日志。";
                StatusChip = "失败";
                ProgressPercent = 0;
                break;
        }
    }

    /// <summary>进度回调：更新百分比与阶段文案（文案对齐原型导出模拟里的三段提示）。</summary>
    private void OnExportProgress(ExportProgress progress)
    {
        ProgressPercent = progress.Percent;
        if (State == NotesPageState.Exporting)
        {
            StatusSubtitle = DescribePhase(progress.Phase);
        }
    }

    /// <summary>把导出阶段翻成给用户看的进度文案。</summary>
    private static string DescribePhase(ExportPhase phase) => phase switch
    {
        ExportPhase.Preparing => "正在准备导出…",
        ExportPhase.ComputingRange => "正在计算变更范围…",
        ExportPhase.ScanningConflicts => "正在检查文件系统冲突…",
        ExportPhase.WritingSnapshots => "正在写出「更新前」「更新后」快照…",
        ExportPhase.WritingNotes => "正在生成更新说明.md…",
        _ => "正在导出…",
    };

    /// <summary>耗时展示：「2.4 秒」；不足一秒时给出毫秒量级，避免显示「0.0 秒」。</summary>
    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalSeconds >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:0.0} 秒")
        : string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalMilliseconds:0} 毫秒");

    /// <summary>解析输出路径：本次行内改的值优先，其次每仓库上次，最后全局默认（用户故事 43）。</summary>
    private void ResolveOutputPath()
    {
        if (_session.OpenedRepository is not { } repository)
        {
            OutputPath = string.Empty;
            return;
        }

        OutputPath = OutputPathResolver.Resolve(
            _session.SessionOutputPath,
            _settings.GetRepositoryState(repository.RootPath).LastOutputPath,
            _settings.Settings.DefaultOutputPath);
    }

    /// <summary>
    /// 导出成功后把本次实际使用的输出路径写回该仓库的「上次输出路径」（用户故事 43）。
    /// 时机选在**导出成功后**而不是页面里改一次就写：第 3 步的行内更改只是「本次」档位，
    /// 只有真的用它导出过，才值得被这个仓库记住。值取输出路径（不含更新日期目录，与记忆语义一致）；
    /// 取消、失败、空范围与冲突中止都不会走到这里。写入失败由设置服务降级为 Warning。
    /// </summary>
    private void RememberOutputPath()
    {
        if (_session.OpenedRepository is not { } repository || OutputPath.Length == 0)
        {
            return;
        }

        var state = _settings.GetRepositoryState(repository.RootPath);
        if (string.Equals(state.LastOutputPath, OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            // 本次用的就是记住的那个路径（最常见的情形）：没必要为同一个值再写一次盘。
            return;
        }

        _settings.SaveRepositoryState(repository.RootPath, state with { LastOutputPath = OutputPath });
        _logger.Information(
            "已记住该仓库的输出路径：{RepositoryPath} → {OutputPath}",
            repository.RootPath,
            OutputPath);
    }

    /// <summary>回到「可以导出」的初始态（进入页面或重算前调用）。</summary>
    private void ResetToReadyState()
    {
        State = NotesPageState.Ready;
        StatusTitle = "准备就绪";
        StatusSubtitle = "确认更新日期后即可导出更新包。";
        StatusChip = "待导出";
        ProgressPercent = 0;
        ShowCompletionPrompt = false;
        NeedsInsideRepositoryWarning = false;
        Conflicts = [];
        _outputPackagePath = null;
    }

    /// <summary>把预检算出的更新包结构落到界面上（含重名让位后的实际目录名，用户故事 31）。</summary>
    private void ApplyPlan(UpdatePackagePlan plan)
    {
        BeforeFileCount = plan.BeforeFileCount;
        AfterFileCount = plan.AfterFileCount;
        AddedFileCount = plan.AddedFileCount;
        DeletedFileCount = plan.DeletedFileCount;
        HasStructure = true;

        // 目录名被占用时 Core 已经让位成 _HHmmss：直接显示实际要用的名字，
        // 用户一眼看到「本次会落到哪」，不必等导出完成才知道。
        if (!string.Equals(plan.FolderName, FolderName, StringComparison.Ordinal))
        {
            FolderName = plan.FolderName;
        }
    }

    /// <summary>不可导出的状态（未选输出路径 / 空范围 / git 失败）：给出原因并禁止导出。</summary>
    private void ShowUnavailable(string message)
    {
        State = NotesPageState.Failed;
        StatusTitle = "无法导出";
        StatusSubtitle = message;
        StatusChip = "不可导出";
    }

    /// <summary>按当前会话与页面输入构造导出请求（时间戳与默认目录名同源；编辑器内容作为本次导出专用稿）。</summary>
    private ExportRequest CreateRequest()
    {
        var repository = _session.OpenedRepository!;
        var range = _session.SelectedRange!;

        return new ExportRequest(
            repository.RootPath,
            OutputPath,
            FolderName,
            _openedAt,
            repository.CurrentBranch ?? "HEAD",
            range.Base,
            range.Head)
        {
            NotesOverride = EditorText,
        };
    }

    /// <summary>范围取自会话，进入页面时刷新与它相关的展示属性。</summary>
    private void NotifyRangeChanged()
    {
        OnPropertyChanged(nameof(HasRange));
        OnPropertyChanged(nameof(RangeText));
    }
}
