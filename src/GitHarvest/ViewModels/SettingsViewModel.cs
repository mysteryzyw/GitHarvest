using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Settings;
using GitHarvest.Core.Templates;
using Serilog;

namespace GitHarvest.ViewModels;

/// <summary>
/// 「外观主题」分段控件的一项。选中态由 <see cref="IsSelected"/> 驱动（与预览页类型筛选同一套做法），
/// XAML 只负责把选中态画出来，不参与「选了谁」的判断。
/// </summary>
public sealed partial class ThemeOptionItem : ObservableObject
{
    /// <param name="option">这一项代表的主题档位。</param>
    /// <param name="title">分段按钮上的文字。</param>
    public ThemeOptionItem(AppThemeOption option, string title)
    {
        Option = option;
        Title = title;
    }

    /// <summary>这一项代表的主题档位。</summary>
    public AppThemeOption Option { get; }

    /// <summary>分段按钮上的文字（浅色 / 深色 / 跟随系统）。</summary>
    public string Title { get; }

    /// <summary>是否为当前生效的档位。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// 设置页里需要**二次确认**的动作。这些动作会动磁盘上的数据（复制整份数据、删文件），
/// 因此都得先弹一次确认浮层；重置全部数据还会额外标出「不可撤销」。
/// </summary>
public enum SettingsConfirmation
{
    /// <summary>没有待确认的动作（浮层隐藏）。</summary>
    None,

    /// <summary>把数据目录迁到用户选的新目录。</summary>
    MigrateDataDirectory,

    /// <summary>清理导出历史与日志（保留设置与最近仓库）。</summary>
    ClearHistoryAndLogs,

    /// <summary>重置全部用户数据（等于恢复出厂）。</summary>
    ResetAllData,
}

/// <summary>
/// 全局设置页（并列工具页）的状态：四项全局设置（输出路径/说明模板/主题/git 路径）
/// + 数据与日志的管理（数据保留天数、改数据目录、删数据）。
/// 保存口径是**即时保存**（原型没有保存按钮）：文本框在失焦或回车时提交，
/// 主题与「恢复内置」这类点击即改的项点了就写盘，每次写入都立刻持久化到 settings.json。
/// 只依赖 Core 的接口——设置读写走 <see cref="ISettingsService"/>，主题走 <see cref="IThemeService"/>，
/// 数据目录位置/迁移/清理走 <see cref="IDataLocation"/>、<see cref="IDataMigrationService"/>、
/// <see cref="IDataMaintenanceService"/>；本类不碰文件系统与画面。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGitEnvironmentService _gitEnvironment;
    private readonly IThemeService _theme;
    private readonly ITemplateService _templates;
    private readonly IDataLocation _dataLocation;
    private readonly IDataMigrationService _dataMigration;
    private readonly IDataMaintenanceService _dataMaintenance;
    private readonly IFolderPicker _folderPicker;
    private readonly IFolderOpener _folderOpener;
    private readonly ILogger _logger;

    public SettingsViewModel(
        ShellViewModel shell,
        ISettingsService settings,
        IGitEnvironmentService gitEnvironment,
        IThemeService theme,
        ITemplateService templates,
        IDataLocation dataLocation,
        IDataMigrationService dataMigration,
        IDataMaintenanceService dataMaintenance,
        IFolderPicker folderPicker,
        IFolderOpener folderOpener,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(dataLocation);
        ArgumentNullException.ThrowIfNull(dataMigration);
        ArgumentNullException.ThrowIfNull(dataMaintenance);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(folderOpener);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        _settings = settings;
        _gitEnvironment = gitEnvironment;
        _theme = theme;
        _templates = templates;
        _dataLocation = dataLocation;
        _dataMigration = dataMigration;
        _dataMaintenance = dataMaintenance;
        _folderPicker = folderPicker;
        _folderOpener = folderOpener;
        _logger = logger;

        ThemeOptions =
        [
            new ThemeOptionItem(AppThemeOption.Light, "浅色"),
            new ThemeOptionItem(AppThemeOption.Dark, "深色"),
            new ThemeOptionItem(AppThemeOption.FollowSystem, "跟随系统"),
        ];

        FillFromSettings();
    }

    /// <summary>外壳状态（页面标题等）。</summary>
    public ShellViewModel Shell { get; }

    // ==================== 默认输出路径 ====================

    /// <summary>默认输出路径的输入框内容；失焦或回车时提交保存。</summary>
    [ObservableProperty]
    private string _defaultOutputPath = string.Empty;

    /// <summary>「浏览…」：选一个目录作为默认输出路径，选完即保存。</summary>
    [RelayCommand]
    private void BrowseOutputPath()
    {
        var picked = _folderPicker.PickFolder(
            DefaultOutputPath.Length > 0 ? DefaultOutputPath : null);
        if (picked is null)
        {
            return;
        }

        DefaultOutputPath = picked;
        CommitOutputPath();
    }

    /// <summary>提交默认输出路径（失焦 / 回车）。值没变则什么都不做，避免无谓的写盘与提示。</summary>
    [RelayCommand]
    private void CommitOutputPath()
    {
        var trimmed = DefaultOutputPath.Trim();
        DefaultOutputPath = trimmed;

        if (string.Equals(trimmed, _settings.Settings.DefaultOutputPath, StringComparison.Ordinal))
        {
            return;
        }

        Save(_settings.Settings with { DefaultOutputPath = trimmed });
    }

    // ==================== 默认说明模板 ====================

    /// <summary>
    /// 默认说明模板的路径（留空即用内置模板）；失焦或回车时提交保存。
    /// 这一行不是「选一个模板文件」的对话框：模板是 .md 文本，写成什么样由用户决定，
    /// 因此路径可填可改，「打开模板文件」只负责把他送到文件跟前（决议 2）。
    /// </summary>
    [ObservableProperty]
    private string _templatePath = string.Empty;

    /// <summary>是否配置了自定义模板（决定「恢复内置」是否可用；「打开模板文件」另看文件是否存在）。</summary>
    public bool HasCustomTemplate => _settings.Settings.DefaultTemplatePath.Length > 0;

    /// <summary>
    /// 「打开模板文件」是否可用：配置了模板路径**且文件当前存在**（验收 12:140）。
    /// 只判路径非空会让「文件已被移走」的按钮照常可点、点了才报错，所以存在性在这里一并判。
    /// </summary>
    public bool CanOpenTemplateFile => _templates.IsTemplateFileAvailable(_settings.Settings.DefaultTemplatePath);

    /// <summary>
    /// 模板文件是否「填了路径但已不存在」（驱动该行的动态提示：当前实际走的是内置模板）。
    /// 「已配置」与 Core 同口径用 <c>IsNullOrWhiteSpace</c>：手改 settings.json 塞进纯空白路径时
    /// Core 视作走内置，这里若按非空判就会误报「文件缺失」。未配置路径时不提示——
    /// 那一行的静态说明「留空即使用内置模板」已经把话说在前头。
    /// </summary>
    public bool IsTemplateFileMissing
        => !string.IsNullOrWhiteSpace(_settings.Settings.DefaultTemplatePath) && !CanOpenTemplateFile;

    /// <summary>刷新模板行的可用性与提示（路径提交 / 保存 / 重填界面后调用；判定很便宜，多刷无害）。</summary>
    private void RefreshTemplateState()
    {
        OnPropertyChanged(nameof(HasCustomTemplate));
        OnPropertyChanged(nameof(CanOpenTemplateFile));
        OnPropertyChanged(nameof(IsTemplateFileMissing));
        OpenTemplateFileCommand.NotifyCanExecuteChanged();
        RestoreBuiltInTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>提交模板路径（失焦 / 回车）。</summary>
    [RelayCommand]
    private void CommitTemplatePath()
    {
        var trimmed = TemplatePath.Trim();
        TemplatePath = trimmed;

        if (string.Equals(trimmed, _settings.Settings.DefaultTemplatePath, StringComparison.Ordinal))
        {
            // 值没变也要刷一次：文件可能在页面开着的时候被外部移走 / 放回，
            // 提交动作是用户手里现成的「再确认一下」时机。
            RefreshTemplateState();
            return;
        }

        if (Save(_settings.Settings with { DefaultTemplatePath = trimmed }))
        {
            RefreshTemplateState();
        }
    }

    /// <summary>
    /// 「打开模板文件」：在系统文件管理器里定位到当前的模板文件，用户用自己习惯的编辑器改。
    /// 可用性已把「路径非空 + 文件存在」都判掉（验收 12:140），这里的两个分支只是兜底：
    /// 存在性检查与真正打开之间文件可能恰好被移走。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenTemplateFile))]
    private void OpenTemplateFile()
    {
        var path = _settings.Settings.DefaultTemplatePath;
        if (path.Length == 0)
        {
            return;
        }

        if (!_folderOpener.RevealFile(path))
        {
            ErrorMessage = $"找不到模板文件：{path}。请确认它还在原位置，或点「恢复内置」改用内置模板。";
            _logger.Warning("定位模板文件失败：{TemplatePath}", path);
            return;
        }

        ShowStatus("已在资源管理器中定位模板文件。");
    }

    /// <summary>
    /// 「恢复内置」：清空模板路径（settings.json 里为空即走内置模板）。
    /// 第 4 步下次进入或用「重置为模板」时会重新加载，无需其他动作。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCustomTemplate))]
    private void RestoreBuiltInTemplate()
    {
        TemplatePath = string.Empty;
        Save(_settings.Settings with { DefaultTemplatePath = string.Empty });
        ShowStatus("已恢复内置模板，更新说明页下次进入即生效。");
    }

    // ==================== git.exe 路径 ====================

    /// <summary>手动指定的 git.exe 路径（可为完整路径，也可为 Git 安装目录）；失焦或回车时保存并复验。</summary>
    [ObservableProperty]
    private string _gitExecutablePath = string.Empty;

    /// <summary>git 复验结果文案（生效 / 未生效 / 无效）。</summary>
    [ObservableProperty]
    private string _gitStatusText = string.Empty;

    /// <summary>复验结果是否为成功态（决定文案配色）。</summary>
    [ObservableProperty]
    private bool _isGitStatusSuccess;

    /// <summary>「浏览…」：选择 git.exe 所在目录（Git 安装目录也可，探测会自己补 git.exe）。</summary>
    [RelayCommand]
    private void BrowseGitExecutable()
    {
        var picked = _folderPicker.PickFolder(
            GitExecutablePath.Length > 0 ? GitExecutablePath : null);
        if (picked is null)
        {
            return;
        }

        GitExecutablePath = picked;
        _ = CommitGitExecutableAsync();
    }

    /// <summary>
    /// 提交 git.exe 路径（失焦 / 回车）：先写盘，再强制重新探测（<c>git --version</c>）把结果摆到界面上。
    /// 探测会先试手动路径、失败回落 PATH 与常见安装路径，因此要按**实际生效的来源**给结论——
    /// 手动路径写错了但机器上另有一份可用的 git 时，不能报「成功」让用户以为自己的路径生效了。
    /// </summary>
    [RelayCommand]
    private async Task CommitGitExecutableAsync()
    {
        var trimmed = GitExecutablePath.Trim();
        GitExecutablePath = trimmed;

        if (!string.Equals(trimmed, _settings.Settings.GitExecutablePath, StringComparison.Ordinal))
        {
            if (!Save(_settings.Settings with { GitExecutablePath = trimmed }))
            {
                return;
            }
        }

        GitEnvironmentStatus status;
        try
        {
            status = await _gitEnvironment.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // 复验没有界面取消入口，这是窗口关闭等场景的兜底。
            IsGitStatusSuccess = false;
            GitStatusText = "检测被取消，请重试。";
            return;
        }

        IsGitStatusSuccess = status is { IsAvailable: true, Source: GitExecutableSource.ManualSetting };
        GitStatusText = DescribeGitStatus(status, trimmed);
    }

    /// <summary>把 git 自检结果翻成这一行下面的一句人话（区分「手动路径生效」「回落自动探测」「不可用」）。</summary>
    private static string DescribeGitStatus(GitEnvironmentStatus status, string manualPath) => status switch
    {
        { IsAvailable: true, Source: GitExecutableSource.ManualSetting } =>
            $"已生效：{status.Version}（{status.ExecutablePath}）",
        { IsAvailable: true } =>
            $"手动路径未生效，当前使用自动探测到的 git：{status.Version}（{status.ExecutablePath}）"
            + (manualPath.Length > 0 ? "。请检查路径是否指向真正的 git.exe。" : string.Empty),
        _ => status.Guidance ?? "git 不可用。",
    };

    // ==================== 外观主题 ====================

    /// <summary>当前主题档位（与设置同步）。</summary>
    [ObservableProperty]
    private AppThemeOption _selectedTheme;

    /// <summary>分段控件的三个选项。</summary>
    public IReadOnlyList<ThemeOptionItem> ThemeOptions { get; }

    /// <summary>
    /// 选择主题：立即应用到界面（不重启），并写回设置。
    /// 应用失败不影响保存——下次启动仍按用户选的档位尝试。
    /// </summary>
    [RelayCommand]
    private void SelectTheme(ThemeOptionItem? item)
    {
        if (item is null || item.Option == SelectedTheme)
        {
            return;
        }

        SelectedTheme = item.Option;
        RefreshThemeSelection();
        _theme.Apply(item.Option);
        Save(_settings.Settings with { DefaultTheme = item.Option });
    }

    /// <summary>把选中态同步到三个分段按钮上。</summary>
    private void RefreshThemeSelection()
    {
        foreach (var option in ThemeOptions)
        {
            option.IsSelected = option.Option == SelectedTheme;
        }
    }

    // ==================== 数据与日志 ====================

    /// <summary>
    /// 数据目录的显示形式：默认位置仍显示成原型的 <c>%APPDATA%\GitHarvest\</c>
    /// （用户认得这个写法、也方便照着找），自定义位置则显示真实路径。
    /// </summary>
    public string DataDirectoryDisplay => _dataLocation.IsCustom
        ? _dataLocation.DataDirectory
        : @"%APPDATA%\GitHarvest\";

    /// <summary>数据目录的真实绝对路径（悬停提示与「已迁移到」提示里用）。</summary>
    public string DataDirectoryFullPath => _dataLocation.DataDirectory;

    /// <summary>数据目录这一行的补充说明：默认位置还是自定义位置。</summary>
    public string DataDirectoryHint => _dataLocation.IsCustom
        ? "（自定义位置）在资源管理器中打开的目录由下方的按钮决定；日志与导出历史都跟着走。"
        : "（默认位置）设置、按仓库记忆、导出历史与运行日志都在这里。";

    /// <summary>日志目录的绝对路径：这一行下面显示它，也给「打开日志目录」的悬停提示用。</summary>
    public string LogDirectoryFullPath => _dataLocation.LogDirectory;

    /// <summary>数据保留天数（0 = 不自动清理）；失焦或回车时提交。</summary>
    [ObservableProperty]
    private int _dataRetentionDays;

    /// <summary>保留天数这一行的当前口径说明。</summary>
    public string RetentionHint => DataRetentionDays <= 0
        ? "当前：不自动清理，日志与导出历史一直保留（手动清理按钮仍可用）。"
        : $"当前：启动时自动清理 {DataRetentionDays} 天前的日志文件与导出历史。";

    partial void OnDataRetentionDaysChanged(int value) => OnPropertyChanged(nameof(RetentionHint));

    /// <summary>
    /// 提交保留天数（失焦 / 回车）：保存后**立即清理一次**，
    /// 否则用户把 30 天改成 7 天会以为马上生效、实际要等下次启动。
    /// </summary>
    [RelayCommand]
    private void CommitDataRetentionDays()
    {
        // 合法范围 0..3650（0 = 不清理，上限十年）：手改 settings.json 写进四位数以外的值按边界处理。
        var clamped = Math.Clamp(DataRetentionDays, 0, 3650);
        DataRetentionDays = clamped;

        if (_settings.Settings.DataRetentionDays != clamped)
        {
            if (!Save(_settings.Settings with { DataRetentionDays = clamped }))
            {
                return;
            }
        }

        var result = _dataMaintenance.PruneExpired(clamped, DateTimeOffset.Now);
        if (result.DeletedFileCount > 0 || result.RemovedHistoryCount > 0)
        {
            ShowStatus(
                $"已按 {clamped} 天清理：删除 {result.DeletedFileCount} 个文件、{result.RemovedHistoryCount} 条导出历史。",
                warning: result.SkippedFiles.Count > 0);
        }
    }

    /// <summary>「更改目录…」：选一个新目录，然后弹确认（迁移是批量复制，必须先跟用户讲清楚）。</summary>
    [RelayCommand]
    private void BrowseDataDirectory()
    {
        var picked = _folderPicker.PickFolder(_dataLocation.DataDirectory);
        if (picked is null)
        {
            return;
        }

        PendingDataDirectory = picked;
        PendingConfirmation = SettingsConfirmation.MigrateDataDirectory;
    }

    /// <summary>「清理导出历史与日志…」：弹确认（会删文件，不可撤销）。</summary>
    [RelayCommand]
    private void RequestClearHistoryAndLogs() => PendingConfirmation = SettingsConfirmation.ClearHistoryAndLogs;

    /// <summary>「重置全部数据…」：弹确认（恢复出厂，不可撤销）。</summary>
    [RelayCommand]
    private void RequestResetAllData() => PendingConfirmation = SettingsConfirmation.ResetAllData;

    /// <summary>确认浮层：取消。</summary>
    [RelayCommand]
    private void CancelPendingAction()
    {
        PendingConfirmation = SettingsConfirmation.None;
        PendingDataDirectory = null;
    }

    /// <summary>确认浮层：按当前待确认的动作执行。</summary>
    [RelayCommand]
    private void ConfirmPendingAction()
    {
        var action = PendingConfirmation;
        PendingConfirmation = SettingsConfirmation.None;

        switch (action)
        {
            case SettingsConfirmation.MigrateDataDirectory:
                MigrateDataDirectory();
                break;
            case SettingsConfirmation.ClearHistoryAndLogs:
                ClearHistoryAndLogs();
                break;
            case SettingsConfirmation.ResetAllData:
                ResetAllData();
                break;
            default:
                break;
        }

        PendingDataDirectory = null;
    }

    /// <summary>
    /// 迁移数据目录：把现有数据复制到新目录、写指针，然后提示重启。
    /// **不热切换**——Serilog 的文件 sink 已经绑在旧日志目录上，热切换会得到一半新一半旧的错乱状态。
    /// </summary>
    private void MigrateDataDirectory()
    {
        if (PendingDataDirectory is not { } target)
        {
            return;
        }

        var result = _dataMigration.MigrateTo(target);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.FailureMessage;
            return;
        }

        OnPropertyChanged(nameof(DataDirectoryDisplay));
        OnPropertyChanged(nameof(DataDirectoryFullPath));
        OnPropertyChanged(nameof(DataDirectoryHint));
        OnPropertyChanged(nameof(LogDirectoryFullPath));

        ShowStatus(result.CopiedFileCount > 0
            ? $"已把 {result.CopiedFileCount} 个文件（{FileSizeFormatter.Format(result.CopiedBytes)}）复制到 {result.TargetDirectory}；"
              + "原目录已保留，确认无误后可自行删除。重启应用后在新位置读写。"
            : $"数据目录已设为 {result.TargetDirectory}（此前没有数据需要复制）。重启应用后生效。");
    }

    /// <summary>清理导出历史与日志（保留设置与最近仓库）。</summary>
    private void ClearHistoryAndLogs()
    {
        var result = _dataMaintenance.ClearHistoryAndLogs();
        if (!result.IsSuccess)
        {
            ErrorMessage = result.FailureMessage;
            return;
        }

        ShowStatus(DescribeMaintenance("已清理", result), warning: result.SkippedFiles.Count > 0);
    }

    /// <summary>
    /// 重置全部数据（等于恢复出厂）：数据文件被删掉后设置会按默认值重新生成，
    /// 因此界面上的各项设置、主题与保留天数都要跟着回到默认值，否则界面显示旧值、盘上已是默认值。
    /// </summary>
    private void ResetAllData()
    {
        var result = _dataMaintenance.ResetAllData();
        if (!result.IsSuccess)
        {
            ErrorMessage = result.FailureMessage;
            return;
        }

        FillFromSettings();
        _theme.Apply(SelectedTheme);

        ShowStatus(
            DescribeMaintenance("已重置", result) + "设置已恢复默认值。",
            warning: result.SkippedFiles.Count > 0);
    }

    /// <summary>把「删了多少、跳过了多少」翻成一句给用户看的话。</summary>
    private static string DescribeMaintenance(string action, DataMaintenanceResult result)
    {
        var summary = $"{action} {result.DeletedFileCount} 个文件（{FileSizeFormatter.Format(result.DeletedBytes)}）";

        if (result.RemovedHistoryCount > 0)
        {
            summary += $"、{result.RemovedHistoryCount} 条导出历史";
        }

        summary += "。";

        if (result.SkippedFiles.Count > 0)
        {
            // 被占用的文件（通常是正在写的当天日志）删不掉：如实说出来，别让用户以为清干净了。
            summary += $"有 {result.SkippedFiles.Count} 个文件正被占用未能删除（如当天日志），关闭其他实例后可再试。";
        }

        return summary;
    }

    /// <summary>「打开日志目录」：在系统文件管理器里打开**日志目录**（spec 用户故事 46：排查错误时一键到日志跟前）。</summary>
    [RelayCommand]
    private void OpenLogDirectory()
    {
        var directory = _dataLocation.LogDirectory;
        if (_folderOpener.Open(directory))
        {
            ShowStatus("已打开日志目录。");
            return;
        }

        ErrorMessage = $"无法打开日志目录，请手动前往：{directory}";
        _logger.Warning("打开日志目录失败：{LogDirectory}", directory);
    }

    // ==================== 二次确认浮层 ====================

    /// <summary>当前待确认的动作；<see cref="SettingsConfirmation.None"/> 表示浮层隐藏。</summary>
    [ObservableProperty]
    private SettingsConfirmation _pendingConfirmation;

    /// <summary>待迁移的目标目录（只有迁移动作会用到）。</summary>
    [ObservableProperty]
    private string? _pendingDataDirectory;

    /// <summary>是否显示确认浮层。</summary>
    public bool HasPendingConfirmation => PendingConfirmation != SettingsConfirmation.None;

    /// <summary>确认浮层的标题。</summary>
    public string ConfirmationTitle => PendingConfirmation switch
    {
        SettingsConfirmation.MigrateDataDirectory => "迁移数据目录",
        SettingsConfirmation.ClearHistoryAndLogs => "清理导出历史与日志",
        SettingsConfirmation.ResetAllData => "重置全部数据",
        _ => string.Empty,
    };

    /// <summary>确认浮层的正文：把「会发生什么」讲清楚，尤其是不可逆的部分。</summary>
    public string ConfirmationMessage => PendingConfirmation switch
    {
        SettingsConfirmation.MigrateDataDirectory =>
            $"将把当前数据目录的全部内容复制到：\n{PendingDataDirectory}\n\n"
            + "包括设置、按仓库记忆、导出历史与日志。原目录会保留，确认无误后你可以自己删除。"
            + "迁移后需要重启应用才会在新位置读写。",
        SettingsConfirmation.ClearHistoryAndLogs =>
            "将删除导出历史文件与日志目录下的日志文件。\n"
            + "设置、按仓库记忆与最近仓库列表会保留。\n\n此操作不可撤销。",
        SettingsConfirmation.ResetAllData =>
            "将删除设置、按仓库记忆、导出历史与日志，等于恢复出厂状态。\n"
            + "数据目录的位置配置会保留。\n\n此操作不可撤销。",
        _ => string.Empty,
    };

    /// <summary>确认按钮的文案（动词，别用「确定」——用户要一眼看出会发生什么）。</summary>
    public string ConfirmationActionText => PendingConfirmation switch
    {
        SettingsConfirmation.MigrateDataDirectory => "迁移",
        SettingsConfirmation.ClearHistoryAndLogs => "清理",
        SettingsConfirmation.ResetAllData => "重置全部数据",
        _ => "确定",
    };

    /// <summary>是否是不可撤销的动作（决定确认按钮用危险色）。</summary>
    public bool IsConfirmationDestructive => PendingConfirmation is not SettingsConfirmation.MigrateDataDirectory;

    partial void OnPendingConfirmationChanged(SettingsConfirmation value)
    {
        OnPropertyChanged(nameof(HasPendingConfirmation));
        OnPropertyChanged(nameof(ConfirmationTitle));
        OnPropertyChanged(nameof(ConfirmationMessage));
        OnPropertyChanged(nameof(ConfirmationActionText));
        OnPropertyChanged(nameof(IsConfirmationDestructive));
    }

    /// <summary>把设置里的值填进界面（构造时与「重置全部数据」之后各用一次）。</summary>
    private void FillFromSettings()
    {
        var current = _settings.Settings;

        DefaultOutputPath = current.DefaultOutputPath;
        TemplatePath = current.DefaultTemplatePath;
        GitExecutablePath = current.GitExecutablePath;
        DataRetentionDays = current.DataRetentionDays;
        SelectedTheme = current.DefaultTheme;

        RefreshThemeSelection();
        RefreshTemplateState();
    }

    // ==================== 页内反馈 ====================

    /// <summary>
    /// 需要用户确认的那几个动作的反馈（定位模板文件 / 恢复内置 / 打开日志目录 / 迁移 / 清理）。
    /// **即时保存不写这里**——改一下存一下，每次弹「已保存」是噪声（结果用户看得见）。
    /// 界面上它是一条高亮提示条（与错误/警告横幅同族），因此多一个「要不要按警告显示」的开关：
    /// 清理时若有文件被占用没能删掉，绿底绿字里带一句「有 N 个文件未能删除」会读成自相矛盾。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>这条反馈是否该按警告（黄色）显示。</summary>
    [ObservableProperty]
    private bool _isStatusWarning;

    /// <summary>保存或操作失败的原因；为空表示没有错误。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>是否显示错误条。</summary>
    public bool HasError => ErrorMessage is not null;

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>是否有成功反馈要显示。</summary>
    public bool HasStatusMessage => StatusMessage.Length > 0;

    /// <summary>设置一条反馈（并按需要切成警告色），高亮提示条由它驱动。</summary>
    private void ShowStatus(string message, bool warning = false)
    {
        IsStatusWarning = warning;
        StatusMessage = message;
    }

    /// <summary>
    /// 保存设置并持久化。与每仓库状态不同，全局设置保存失败必须让用户知道
    /// （否则界面显示新值、盘上还是旧值），因此这里捕获设置服务的异常并摆到错误条上。
    /// </summary>
    /// <returns>保存成功返回真。</returns>
    private bool Save(GlobalSettings settings)
    {
        try
        {
            _settings.Save(settings);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorMessage = $"保存设置失败：{exception.Message}。请检查文件是否被占用或有写入权限，然后重试。";
            _logger.Warning(exception, "保存全局设置失败。");
            return false;
        }

        ErrorMessage = null;

        // 保存本身**不给成功提示**：这是即时保存，改一下存一下，每次都弹「已保存」只是噪声
        //（主题当下就变色、路径就在框里，用户看得到结果）。只有需要用户确认的动作
        //（定位模板文件 / 恢复内置 / 打开日志目录）才给一句反馈。
        // 模板那一行的两个按钮可用性与「文件缺失」提示跟着设置走。
        RefreshTemplateState();
        return true;
    }
}
