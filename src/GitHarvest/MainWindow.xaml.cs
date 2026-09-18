using System.Windows;
using GitHarvest.Core.Navigation;
using GitHarvest.Shell;
using GitHarvest.ViewModels;
using Wpf.Ui.Controls;

namespace GitHarvest;

/// <summary>
/// 应用外壳窗口：标题栏 + 左侧导航（工作流四项与全局设置 / 关于）+ 内容区。
/// 导航项在 XAML 中声明，页面与 ViewModel 由 DI 容器解析（见 <see cref="WpfShellNavigator"/>）。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly WpfShellNavigator _navigator;
    private bool _initialNavigationRequested;

    public MainWindow(
        ShellViewModel shellViewModel,
        WpfShellNavigator navigator,
        IServiceProvider services)
    {
        _navigator = navigator;

        DataContext = shellViewModel;
        InitializeComponent();

        _navigator.Attach(RootNavigation, services);

        RootNavigation.Loaded += OnRootNavigationLoaded;
    }

    private void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        // 导航控件在初始化阶段才登记菜单项，所以首次导航要等到 Loaded。
        if (_initialNavigationRequested)
        {
            return;
        }

        _initialNavigationRequested = true;
        _navigator.NavigateTo(ShellPage.Repository);
    }
}
