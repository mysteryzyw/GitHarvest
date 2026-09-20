using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
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

        var logDirectory = AppPaths.GetLogDirectory();
        Log.Logger = LoggingSetup.ConfigureFileLogging(logDirectory);
        Log.Information(
            "GitHarvest 启动，版本 {Version}，日志目录 {LogDirectory}",
            ApplicationVersion,
            logDirectory);

        _services = BuildServices();

        // 显式触发设置加载：首次启动时在此生成 %APPDATA%\GitHarvest\settings.json（含全部默认值），
        // 不依赖 git 探测等后续链路的间接触发。
        _ = _services.GetRequiredService<ISettingsService>();

        // 让 WpfUI 自身的控件也能从同一个容器解析依赖
        ControlsServices.Initialize(_services);

        ApplicationThemeManager.Apply(ApplicationTheme.Light);

        // 强调色固定为原型里的 #0067C0：WpfUI 默认取 Windows 系统强调色，与原型不一致。
        // 这一步会重算整套 Accent 画刷，导航竖条、主按钮、选中态都随之对齐。
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x00, 0x67, 0xC0), ApplicationTheme.Light);

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();

        // git 环境自检：放在窗口显示之后——探测要启动 git 子进程（几十毫秒），不该拖慢首屏。
        // 结果写进日志（版本、来源、路径），首页的引导横幅也复用这份缓存。
        _ = ProbeGitEnvironmentAsync(_services.GetRequiredService<IGitEnvironmentService>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("GitHarvest 退出");
        Log.CloseAndFlush();

        _services?.Dispose();
        _services = null;

        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // 日志器：Core 里的服务（git 自检、设置读写）都注入这一个文件日志器
        services.AddSingleton<ILogger>(Log.Logger);

        // Core 服务：导航目录 + git 访问基础设施（ticket 02）+ 设置与 JSON 持久化（ticket 03）
        // + 仓库级 Git 操作（ticket 04）+ 导出编排（ticket 08：导出前总预览起步）
        services.AddSingleton<INavigationCatalog, NavigationCatalog>();
        services.AddSingleton(provider => GitExecutableLocator.ForCurrentEnvironment());
        services.AddSingleton<GitCliRunner>();
        services.AddSingleton<ISettingsService>(provider => new SettingsService(
            AppPaths.GetSettingsFilePath(),
            AppPaths.GetRepositoryStateFilePath(),
            provider.GetRequiredService<ILogger>()));
        // git 探测只依赖「手动路径」窄接口，由设置服务同一单例充当；保存设置后复验无需改探测代码。
        services.AddSingleton<IGitExecutablePathProvider>(provider => provider.GetRequiredService<ISettingsService>());
        services.AddSingleton<IGitEnvironmentService, GitEnvironmentService>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<IExportService, ExportService>();

        // 壳向 ViewModel 提供的交互接缝：文件夹选择对话框与「打开输出文件夹」
        // （接口定义在 Core，见 IFolderPicker / IFolderOpener）
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IFolderOpener, FolderOpener>();

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
        services.AddTransient<SettingsPage>();
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

    private static string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
}
