using System.Reflection;
using System.Windows;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Navigation;
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

        _services.GetRequiredService<MainWindow>().Show();
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

        // Core 服务：ticket 01 只需要导航目录；git / 设置 / 导出等服务由后续 ticket 注册
        services.AddSingleton<INavigationCatalog, NavigationCatalog>();

        // 外壳与导航
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<WpfShellNavigator>();
        services.AddSingleton<IShellNavigator>(provider => provider.GetRequiredService<WpfShellNavigator>());
        services.AddSingleton<MainWindow>();

        // 页面：由 WpfUI 的导航在切换时从容器解析（页面只声明带依赖的构造函数）
        services.AddTransient<RepositoryPage>();
        services.AddTransient<PickCommitsPage>();
        services.AddTransient<PreviewPage>();
        services.AddTransient<NotesPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();

        return services.BuildServiceProvider();
    }

    private static string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
}
