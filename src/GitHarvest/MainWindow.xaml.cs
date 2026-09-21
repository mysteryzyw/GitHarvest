using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitHarvest.Core.Navigation;
using GitHarvest.Shell;
using GitHarvest.ViewModels;
using H.NotifyIcon;
using Serilog;
using Wpf.Ui.Controls;

namespace GitHarvest;

/// <summary>
/// 应用外壳窗口：标题栏 + 左侧导航（工作流四项与全局设置 / 关于）+ 内容区。
/// 导航项在 XAML 中声明，页面与 ViewModel 由 DI 容器解析（见 <see cref="WpfShellNavigator"/>）。
/// 关闭语义（ticket 15）：点 X 不是退出而是隐藏到系统托盘（首次给气泡提示）；
/// 托盘菜单「退出」或托盘不存在时（<see cref="_realExit"/>）才走真正的关闭与退出。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly WpfShellNavigator _navigator;
    private readonly ILogger _logger;
    private bool _initialNavigationRequested;
    private TaskbarIcon? _trayIcon;
    private bool _realExit;
    private bool _trayHintShown;

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

        // 托盘图标挂在 Loaded 上而不是构造函数：ui-check 的 DI 图检查会**构造** MainWindow
        // 但不 Show 它——构造期不建托盘，离屏检查才不会在系统托盘里留下一个游离图标。
        Loaded += (_, _) => CreateTrayIcon();
    }

    /// <summary>
    /// 创建托盘图标与它的上下文菜单。形状与 exe/标题栏同一份 AppIcon.ico
    /// （绝对 pack URI，见 ticket 13 的入口程序集解析坑）。
    /// </summary>
    private void CreateTrayIcon()
    {
        var openItem = new System.Windows.Controls.MenuItem { Header = "打开 GitHarvest" };
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApplication();
        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = $"GitHarvest {App.ApplicationVersion} —— 本地 Git 仓库变更导出工具",
            IconSource = BitmapFrame.Create(
                new Uri("pack://application:,,,/GitHarvest;component/Assets/AppIcon.ico")),
            ContextMenu = menu,
        };
        // ForceCreate(false)：不让库开「效率模式」——它会压低进程优先级，窗口恢复后的首次交互会发黏；
        // 后台常驻对这个工具不是常态（用户随时会唤回），不值得换那点功耗。
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        _trayIcon.TrayLeftMouseDoubleClick += (_, _) => RestoreFromTray();
    }

    /// <summary>
    /// 关闭拦截（ticket 15）：非真正退出时取消关闭、隐藏到托盘。
    /// 气泡提示只在第一次给——每次都弹是噪声（Windows 自身对托盘驻留型应用也是这么做的）。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_realExit)
        {
            // 真正退出：托盘图标随窗口关闭立即消失（不留给用户一个「点了没反应」的死图标）。
            _trayIcon?.Dispose();
            _trayIcon = null;
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
        _logger.Information("主窗口已最小化到系统托盘。");

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _trayIcon?.ShowNotification(
            "GitHarvest 仍在运行",
            "已最小化到系统托盘（任务栏右下角）。双击托盘图标或右键「打开 GitHarvest」回到窗口，右键「退出」才是真正退出。");
        _logger.Information("已给出首次托盘气泡提示。");
    }

    /// <summary>从托盘恢复窗口并聚焦。唤起信号来自托盘双击/菜单，或第二个实例的启动信号（可能跨线程，须走 Dispatcher）。</summary>
    public void RestoreFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RestoreFromTray);
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        _logger.Information("主窗口已从系统托盘恢复。");
    }

    /// <summary>托盘菜单「退出」：置真退出标志后走正常关闭（OnClosing 不再拦截）。</summary>
    public void ExitApplication()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ExitApplication);
            return;
        }

        _realExit = true;
        Close();
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
