using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.History;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
using System.Globalization;
using System.IO;

namespace GitHarvest.ViewModels;

/// <summary>
/// 仓库页（工作流第 1 步）的状态：打开仓库、最近列表与 git 环境自检的引导。
/// 只依赖 Core 的接口——<see cref="IGitService"/> 负责打开仓库，<see cref="ISettingsService"/>
/// 负责最近列表与全局设置，<see cref="IHistoryService"/> 负责导出历史统计（首页概览与「上次导出」），
/// <see cref="IGitEnvironmentService"/> 负责探测，<see cref="IFolderPicker"/> 由壳实现文件夹选择；
/// 本类不碰进程、文件系统与界面类型。
/// </summary>
public sealed partial class RepositoryViewModel : ObservableObject
{
    private readonly IGitEnvironmentService _gitEnvironment;
    private readonly IGitService _gitService;
    private readonly ISettingsService _settings;
    private readonly IHistoryService _history;
    private readonly IFolderPicker _folderPicker;
    private readonly IShellNavigator _navigator;
    private readonly IRepositorySession _session;

    /// <summary>进入本页时取一次的导出历史统计：同一次渲染里的两个数字必然同源。</summary>
    private readonly ExportHistoryStatistics _historyStatistics;

    /// <param name="shell">外壳状态，页面标题等仍由它提供。</param>
    /// <param name="gitEnvironment">git 环境自检（结果缓存，重复询问不会反复启动进程）。</param>
    /// <param name="gitService">打开仓库的 Git 操作。</param>
    /// <param name="settings">最近仓库列表与全局设置的来源。</param>
    /// <param name="history">导出历史的统计来源（首页概览与最近卡片的「上次导出」）。</param>
    /// <param name="folderPicker">文件夹选择对话框（壳实现）。</param>
    /// <param name="navigator">用于跳到「全局设置」与「选择提交」。</param>
    /// <param name="session">当前仓库会话状态：打开成功后写入，供选择提交等后续步骤读取。</param>
    public RepositoryViewModel(
        ShellViewModel shell,
        IGitEnvironmentService gitEnvironment,
        IGitService gitService,
        ISettingsService settings,
        IHistoryService history,
        IFolderPicker folderPicker,
        IShellNavigator navigator,
        IRepositorySession session)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(session);

        Shell = shell;
        _gitEnvironment = gitEnvironment;
        _gitService = gitService;
        _settings = settings;
        _history = history;
        _folderPicker = folderPicker;
        _navigator = navigator;
        _session = session;

        _historyStatistics = history.GetStatistics();

        ReloadRecentRepositories();
    }

    /// <summary>外壳状态（页面标题、步骤指示条等）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>git 环境自检结果；尚未探测时为 <see langword="null"/>。</summary>
    [ObservableProperty]
    private GitEnvironmentStatus? _gitStatus;

    /// <summary>是否要显示「git 不可用」的引导横幅（探测完成且不可用时）。</summary>
    public bool IsGitUnavailable => GitStatus is { IsAvailable: false };

    /// <summary>引导横幅正文（含怎么安装 Git、去哪里手动指定路径）。</summary>
    public string? GitGuidance => GitStatus?.Guidance;

    /// <summary>路径输入框的内容（「粘贴仓库路径」入口）。</summary>
    [ObservableProperty]
    private string? _manualPath;

    /// <summary>当前打开的仓库；未打开时为 <see langword="null"/>。</summary>
    [ObservableProperty]
    private RepositoryInfo? _openedRepository;

    /// <summary>正在打开仓库（命令执行中），用于禁用打开按钮避免重复提交。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>当前空闲（不在打开仓库的流程中）；作为「打开」命令的 CanExecute 条件。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>打开失败的错误提示（不是 Git 仓库 / 目录不存在 / Git 错误等）。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>打开成功后的警告提示（当前只有提交数不足一种）。</summary>
    [ObservableProperty]
    private string? _warningMessage;

    /// <summary>最近打开的仓库（最新在前，最多 10 条）。</summary>
    [ObservableProperty]
    private IReadOnlyList<RecentRepositoryItem> _recentRepositories = [];

    /// <summary>仓库是否已打开（决定 hero 区显示打开入口还是已打开状态）。</summary>
    public bool IsRepositoryOpen => OpenedRepository is not null;

    /// <summary>当前分支的展示文案：游离头指针时标注「（游离）」。</summary>
    public string CurrentBranchDisplay =>
        OpenedRepository?.CurrentBranch is { } branch ? branch : "（游离）";

    /// <summary>提交数的展示文案（帮助用户理解「提交数不足」的提示）。</summary>
    public string CommitCountDisplay =>
        OpenedRepository is { } repository ? repository.CommitCount.ToString() : "—";

    /// <summary>概览卡：最近仓库数。</summary>
    public string RecentCountDisplay => RecentRepositories.Count.ToString();

    /// <summary>概览卡：历史更新包数（导出历史里的记录条数，用户故事 45）。</summary>
    public string HistoryPackageCountDisplay =>
        _historyStatistics.PackageCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>概览卡：最常用分支（导出历史里出现次数最多的分支）；还没有任何导出时显示「—」。</summary>
    public string MostUsedBranchDisplay => _historyStatistics.MostUsedBranch ?? "—";

    /// <summary>概览卡：默认输出根目录（未设置时明确提示而不是空白）。</summary>
    public string DefaultOutputPathDisplay =>
        string.IsNullOrWhiteSpace(_settings.Settings.DefaultOutputPath)
            ? "未设置"
            : _settings.Settings.DefaultOutputPath;

    /// <summary>页面加载时取一次自检结果（首次真正探测，之后直接命中缓存）。</summary>
    [RelayCommand]
    private async Task CheckGitEnvironmentAsync() => GitStatus = await _gitEnvironment.GetStatusAsync();

    /// <summary>「重新检测」：强制重新探测（设置里改过 git.exe 路径后可用它复验）。</summary>
    [RelayCommand]
    private async Task RefreshGitEnvironmentAsync() => GitStatus = await _gitEnvironment.RefreshAsync();

    /// <summary>「前往全局设置」：去手动指定 git.exe 路径（设置页 UI 在 ticket 12）。</summary>
    [RelayCommand]
    private void OpenSettings() => _navigator.NavigateTo(ShellPage.Settings);

    /// <summary>「下一步：选择提交」：仓库打开后进入工作流第 2 步。</summary>
    [RelayCommand]
    private void GoToPickCommits() => _navigator.NavigateTo(ShellPage.PickCommits);

    /// <summary>
    /// 打开仓库：先确认 git 可用，再交给 <see cref="IGitService"/> 验证并读取仓库概要；
    /// 成功后记住最近列表。传入路径为空时不做任何事（按钮本就该被禁用）。
    /// 失败不抛异常——提示在结果对象里，这里只把它摆到横幅上。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenRepositoryAsync(string? repositoryPath)
    {
        var path = NormalizeInput(repositoryPath);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        ErrorMessage = null;
        WarningMessage = null;
        IsBusy = true;
        try
        {
            var gitStatus = await _gitEnvironment.GetStatusAsync();
            if (!gitStatus.IsAvailable)
            {
                // 横幅上方已有 git 引导，这里再在错误位复述同一引导，保证两个入口都自洽。
                ErrorMessage = gitStatus.Guidance;
                return;
            }

            var result = await _gitService.OpenRepositoryAsync(path);
            // 模式匹配同时确认成功与非空仓库（IsSuccess 是计算属性，编译器看不到它蕴含非空）。
            if (result is not { Repository: { } repository })
            {
                ErrorMessage = result.FailureMessage;
                return;
            }

            OpenedRepository = repository;
            _session.OpenedRepository = repository;
            _settings.AddRecentRepository(repository.RootPath);
            ReloadRecentRepositories();
            OnPropertyChanged(nameof(RecentCountDisplay));

            if (!repository.HasEnoughCommits)
            {
                // 打开照常成功，但要解释清楚为什么不能继续选提交。
                WarningMessage = repository.CommitCount == 0
                    ? "该仓库还没有任何提交，无法构成「基准 + Head」的变更范围；等有提交后再回来导出。"
                    : "该仓库只有 1 个提交，无法构成「基准 + Head」的变更范围（基准与 Head 不能是同一个提交）。";
            }
        }
        catch (OperationCanceledException)
        {
            // 打开仓库目前没有界面取消入口，这是窗口关闭等场景的兜底：收敛成状态而不是让异常上抛。
            ErrorMessage = "打开仓库的操作被取消。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 浏览文件夹：弹出文件夹选择对话框，选中后立即尝试打开
    /// （原型里拖放区点击即进入下一步，这里对齐为「选完就开」）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseFolderAsync()
    {
        var picked = _folderPicker.PickFolder();
        if (picked is null)
        {
            return;
        }

        ManualPath = picked;
        await OpenRepositoryAsync(picked);
    }

    partial void OnGitStatusChanged(GitEnvironmentStatus? value)
    {
        OnPropertyChanged(nameof(IsGitUnavailable));
        OnPropertyChanged(nameof(GitGuidance));
    }

    partial void OnOpenedRepositoryChanged(RepositoryInfo? value)
    {
        OnPropertyChanged(nameof(IsRepositoryOpen));
        OnPropertyChanged(nameof(CurrentBranchDisplay));
        OnPropertyChanged(nameof(CommitCountDisplay));
    }

    partial void OnRecentRepositoriesChanged(IReadOnlyList<RecentRepositoryItem> value)
        => OnPropertyChanged(nameof(RecentCountDisplay));

    partial void OnIsBusyChanged(bool value)
    {
        OpenRepositoryCommand.NotifyCanExecuteChanged();
        BrowseFolderCommand.NotifyCanExecuteChanged();
    }

    /// <summary>从设置重读最近列表（构造时与每次成功打开后调用）。</summary>
    private void ReloadRecentRepositories()
        => RecentRepositories =
        [
            .. _settings.RecentRepositories
                .Select((repositoryPath, index) => ToRecentItem(repositoryPath, index)),
        ];

    /// <summary>把输入路径修剪成可打开的形式：去首尾空白与包裹引号（「复制文件地址」常带引号）。</summary>
    private static string? NormalizeInput(string? repositoryPath)
    {
        var trimmed = repositoryPath?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// 最近列表条目：显示名取路径最后一段；上次选中分支来自每仓库状态、上次导出时间来自导出历史
    /// （原型卡片的 meta 行），两者都没有时分别留空与显示「从未导出」。
    /// 图标色槽按列表序号轮换蓝 / 紫 / 绿（对应原型三张示例卡的配色）。
    /// </summary>
    private RecentRepositoryItem ToRecentItem(string repositoryPath, int index)
        => new(
            repositoryPath,
            Path.GetFileName(repositoryPath.TrimEnd('\\', '/')),
            _settings.GetRepositoryState(repositoryPath).LastBranch,
            FormatLastExport(_history.GetLastExportedAt(repositoryPath)),
            ColorSlot: index % 3);

    /// <summary>
    /// 「上次导出」文案。只到日期（原型就是「上次导出 2026-09-10」）：这一行是列表扫读用的，
    /// 精确到分钟没有意义，反而把卡片挤满；同一天的多次导出看首页的包数就够了。
    /// </summary>
    private static string FormatLastExport(DateTimeOffset? lastExportedAt)
        => lastExportedAt is { } time
            ? $"上次导出 {time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : "从未导出";
}

/// <summary>
/// 首页最近仓库卡片的数据：显示名 + 完整路径 + 上次选中分支 + 上次导出文案 + 图标色槽 0/1/2。
/// </summary>
/// <param name="FullPath">仓库根路径（一键重开时用它）。</param>
/// <param name="DisplayName">卡片标题（路径最后一段）。</param>
/// <param name="LastBranch">上次选中的分支；从未记录过为 <see langword="null"/>（不显示 chip）。</param>
/// <param name="LastExportedDisplay">「上次导出 2026-09-10」或「从未导出」。</param>
/// <param name="ColorSlot">图标配色槽（0/1/2 轮换蓝 / 紫 / 绿）。</param>
public sealed record RecentRepositoryItem(
    string FullPath,
    string DisplayName,
    string? LastBranch,
    string LastExportedDisplay,
    int ColorSlot);
