using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitHarvest.Core.Navigation;
using GitHarvest.Shell;
using GitHarvest.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace GitHarvest;

/// <summary>
/// 应用外壳窗口：标题栏 + 左侧导航（工作流四项与全局设置 / 关于）+ 内容区。
/// 导航项在 XAML 中声明，页面与 ViewModel 由 DI 容器解析（见 <see cref="WpfShellNavigator"/>）。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly WpfShellNavigator _navigator;
    private readonly ILogger _logger;
    private bool _initialNavigationRequested;

    public MainWindow(
        ShellViewModel shellViewModel,
        WpfShellNavigator navigator,
        IServiceProvider services,
        ILogger logger)
    {
        _navigator = navigator;
        _logger = logger;

        DataContext = shellViewModel;
        InitializeComponent();

        _navigator.Attach(RootNavigation, services);

        RootNavigation.Loaded += OnRootNavigationLoaded;
        RootNavigation.Navigated += OnRootNavigationNavigated;
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

    /// <summary>
    /// 每次导航后禁用内容区的内置滚动。必须跟在导航之后：WpfUI 在导航流程里会把
    /// <c>IsDynamicScrollViewerEnabled</c> 重置回默认的 True（实测同一 presenter 实例上
    /// 「导航前设为 False → 导航后回到 True」），因此修的不是属性而是模板。
    /// </summary>
    private void OnRootNavigationNavigated(NavigationView sender, NavigatedEventArgs args)
        => ForceHostContentTemplate();

    /// <summary>
    /// 给导航内容宿主指定「不带内置滚动器」的模板（WpfUI 内置资源
    /// <c>DefaultNavigationViewContentPresenterControlTemplate</c>）：
    /// 默认模板 <c>…WithDynamicScrollViewer…</c> 会用 DynamicScrollViewer 把页面按无限高度包裹——
    /// 页面 ActualHeight 撑到内容全高（实测 949 &gt; 宿主 759），页面自己的 ScrollViewer
    /// 视口等于内容高、滚动条永不出现，高于窗口的内容（如选择提交页的详情卡下半与工作流底栏）
    /// 不可达。换成无滚动器模板后页面高度受宿主约束，滚动交还各页面自带的 ScrollViewer
    /// （各页都自带外层 ScrollViewer；工作流底栏随之固定在窗口底部，与原型语义一致）。
    /// 用 Template 的**本地值**而不是 IsDynamicScrollViewerEnabled 属性：后者是普通 CLR 属性、
    /// set 访问器被标 internal（不能直赋、不能走样式 Setter），且每次导航都会被 WpfUI 重置；
    /// 本地值优先于样式 Trigger，不受该属性复位影响（实测连续三次导航下页面高度始终受约束）。
    /// </summary>
    private void ForceHostContentTemplate()
    {
        if (Application.Current?.TryFindResource("DefaultNavigationViewContentPresenterControlTemplate")
            is not ControlTemplate template)
        {
            // WpfUI 若在未来版本改名/移除该资源，这里会退化成「内容高于窗口时不可滚动」，
            // 留下明确日志便于定位（升级 WpfUI 后请如实机核对页面滚动）。
            _logger.Warning(
                "未能禁用导航内容区的内置滚动：找不到资源 DefaultNavigationViewContentPresenterControlTemplate。");
            return;
        }

        foreach (var presenter in FindVisualChildren<NavigationViewContentPresenter>(this))
        {
            if (!ReferenceEquals(presenter.Template, template))
            {
                presenter.Template = template;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
