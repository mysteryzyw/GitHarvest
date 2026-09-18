using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Navigation;
using GitHarvest.Core.Settings;
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

        // 日志器：Core 里的服务（git 自检、设置读取）都注入这一个文件日志器
        services.AddSingleton<ILogger>(Log.Logger);

        // Core 服务：导航目录 + git 访问基础设施（ticket 02）
        services.AddSingleton<INavigationCatalog, NavigationCatalog>();
        services.AddSingleton(provider => GitExecutableLocator.ForCurrentEnvironment());
        services.AddSingleton<GitCliRunner>();
        services.AddSingleton(provider => new SettingsJsonGitPathProvider(
            AppPaths.GetSettingsFilePath(),
            provider.GetRequiredService<ILogger>()));
        services.AddSingleton<IGitExecutablePathProvider>(provider => provider.GetRequiredService<SettingsJsonGitPathProvider>());
        services.AddSingleton<IGitEnvironmentService, GitEnvironmentService>();

        // 外壳与导航
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<WpfShellNavigator>();
        services.AddSingleton<IShellNavigator>(provider => provider.GetRequiredService<WpfShellNavigator>());
        services.AddSingleton<MainWindow>();

        // 页面与页面级 ViewModel：由 WpfUI 的导航在切换时从容器解析（页面只声明带依赖的构造函数）
        services.AddTransient<RepositoryViewModel>();
        services.AddTransient<RepositoryPage>();
        services.AddTransient<PickCommitsPage>();
        services.AddTransient<PreviewPage>();
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
