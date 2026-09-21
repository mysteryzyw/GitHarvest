using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.History;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
using GitHarvest.Core.Templates;
using GitHarvest.Shell;
using GitHarvest.ViewModels;
using GitHarvest.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace GitHarvest;

/// <summary>
/// 应用启动点，同时是 DI 组合根：先落地文件日志，再建容器、初始化 WpfUI 的主题与容器，最后显示主窗口。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 数据目录先解析：它可能在别处（设置页改过），日志、设置、每仓库状态、导出历史都从它派生。
        // 它在日志起来之前构造，因此容错提示（指针文件坏了、目录被删了）要等日志起来再补记。
        var dataLocation = new DataLocation();

        Log.Logger = LoggingSetup.ConfigureFileLogging(dataLocation.LogDirectory);
        Log.Information(
            "GitHarvest 启动，版本 {Version}，数据目录 {DataDirectory}",
            ApplicationVersion,
            dataLocation.DataDirectory);

        if (dataLocation.Notice is { } locationNotice)
        {
            Log.Warning("数据目录：{Notice}", locationNotice);
        }

        _services = BuildServices(dataLocation);

        // 显式触发设置加载：首次启动时在此生成数据目录下的 settings.json（含全部默认值），
        // 不依赖 git 探测等后续链路的间接触发。
        var settings = _services.GetRequiredService<ISettingsService>();

        // 让 WpfUI 自身的控件也能从同一个容器解析依赖
        ControlsServices.Initialize(_services);

        var window = _services.GetRequiredService<MainWindow>();

        // 主题：按设置里的默认主题（浅色/深色/跟随系统）在**显示窗口之前**应用，
        // 免得先闪一帧浅色再跳成深色。「跟随系统」会在这里把窗口交给 WpfUI 的主题监视器，
        // 因此必须先有窗口实例再应用主题。强调色也由它统一覆盖为原型的 #0067C0
        // （WpfUI 默认取系统强调色，与原型不一致）。
        _services.GetRequiredService<IThemeService>().Apply(settings.Settings.DefaultTheme);

        window.Show();

        // git 环境自检：放在窗口显示之后——探测要启动 git 子进程（几十毫秒），不该拖慢首屏。
        // 结果写进日志（版本、来源、路径），首页的引导横幅也复用这份缓存。
        _ = ProbeGitEnvironmentAsync(_services.GetRequiredService<IGitEnvironmentService>());

        // 定期清理：按设置里的保留天数删掉过期的日志与导出历史。
        // 必须放在日志初始化之后——清理要能跳过正被 Serilog 写着的当天日志文件。
        _ = PruneExpiredDataAsync(
            _services.GetRequiredService<IDataMaintenanceService>(),
            settings.Settings.DataRetentionDays);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("GitHarvest 退出");
        Log.CloseAndFlush();

        _services?.Dispose();
        _services = null;

        base.OnExit(e);
    }

    private static ServiceProvider BuildServices(IDataLocation dataLocation)
    {
        var services = new ServiceCollection();

        // 日志器：Core 里的服务（git 自检、设置读写）都注入这一个文件日志器
        services.AddSingleton<ILogger>(Log.Logger);

        // Core 服务：导航目录 + git 访问基础设施（ticket 02）+ 设置与 JSON 持久化（ticket 03）
        // + 仓库级 Git 操作（ticket 04）+ 导出编排（ticket 08：导出前总预览起步）
        // + 导出历史（ticket 11：导出时追加、首页统计来源）
        // + 数据目录位置/迁移/清理（ticket 14：设置页可改路径、删数据、按天数定期清理）
        services.AddSingleton<INavigationCatalog, NavigationCatalog>();
        services.AddSingleton(dataLocation);
        services.AddSingleton<IDataLocation>(dataLocation);
        services.AddSingleton(provider => GitExecutableLocator.ForCurrentEnvironment());
        services.AddSingleton<GitCliRunner>();
        services.AddSingleton<ISettingsService>(provider => new SettingsService(
            dataLocation.SettingsFilePath,
            dataLocation.RepositoryStateFilePath,
            provider.GetRequiredService<ILogger>()));
        // git 探测只依赖「手动路径」窄接口，由设置服务同一单例充当；保存设置后复验无需改探测代码。
        services.AddSingleton<IGitExecutablePathProvider>(provider => provider.GetRequiredService<ISettingsService>());
        services.AddSingleton<IGitEnvironmentService, GitEnvironmentService>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<IHistoryService>(provider => new HistoryService(
            dataLocation.ExportHistoryFilePath,
            provider.GetRequiredService<ILogger>()));
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IDataMigrationService, DataMigrationService>();
        services.AddSingleton<IDataMaintenanceService, DataMaintenanceService>();

        // 壳向 ViewModel 提供的交互接缝：文件夹选择对话框与「打开输出文件夹」
        // （接口定义在 Core，见 IFolderPicker / IFolderOpener）
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IFolderOpener, FolderOpener>();

        // 主题（ticket 12）：设置页改档即时生效，启动时也经它应用设置里的默认主题
        services.AddSingleton<IThemeService, ThemeService>();

        // 当前打开的仓库会话状态（ticket 06：仓库页写入，选择提交等后续步骤读取）
        services.AddSingleton<IRepositorySession, RepositorySession>();

        // 会话级的一次性警告门（用户故事 33：同一会话内只提示一次）
        services.AddSingleton<IExportWarningGate, ExportWarningGate>();

        // 外壳与导航
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<WpfShellNavigator>();
        services.AddSingleton<IShellNavigator>(provider => provider.GetRequiredService<WpfShellNavigator>());
        services.AddSingleton<MainWindow>();

        // 页面与页面级 ViewModel：由 WpfUI 的导航在切换时从容器解析（页面只声明带依赖的构造函数）
        services.AddTransient<RepositoryViewModel>();
        services.AddTransient<RepositoryPage>();
        services.AddTransient<PickCommitsViewModel>();
        services.AddTransient<PickCommitsPage>();
        services.AddTransient<PreviewViewModel>();
        services.AddTransient<PreviewPage>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NotesPage>();
        // 工具页（ticket 12）：设置页与关于页各有一个自己的 VM，页面构造时由容器注入
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<AboutPage>();

        return services.BuildServiceProvider();
    }

    /// <summary>启动时的 git 环境自检；结果进日志，失败不影响启动（首页横幅会引导用户处理）。</summary>
    private static async Task ProbeGitEnvironmentAsync(IGitEnvironmentService gitEnvironment)
    {
        try
        {
            await gitEnvironment.GetStatusAsync();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "git 环境自检失败。");
        }
    }

    /// <summary>
    /// 启动时按保留天数清理一次过期的日志与导出历史。放后台线程：删文件是 IO，
    /// 几分钟的启动窗口不该被它拖慢；清理失败只记日志（不是启动的前置条件）。
    /// </summary>
    private static async Task PruneExpiredDataAsync(IDataMaintenanceService maintenance, int retentionDays)
    {
        try
        {
            await Task.Run(() => maintenance.PruneExpired(retentionDays, DateTimeOffset.Now));
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "启动时清理过期数据失败，本次跳过。");
        }
    }

    private static string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
}
