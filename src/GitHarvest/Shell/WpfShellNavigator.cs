using GitHarvest.Core.Navigation;
using Wpf.Ui.Controls;

namespace GitHarvest.Shell;

/// <summary>
/// <see cref="IShellNavigator"/> 的 WpfUI 实现：把导航目录中的目的地映射到页面，
/// 并经由 NavigationView 的 DI 容器解析页面实例（页面与 ViewModel 都由容器提供）。
/// 公开可见性是为了让外层窗口能直接注入并绑定导航宿主。
/// </summary>
public sealed class WpfShellNavigator : IShellNavigator
{
    private NavigationView? _navigationView;

    /// <inheritdoc />
    public ShellPage Current { get; private set; } = ShellPage.Repository;

    /// <inheritdoc />
    public event EventHandler<ShellPage>? Navigated;

    /// <summary>绑定导航宿主并把页面解析交给容器，之后即可导航。</summary>
    public void Attach(NavigationView navigationView, IServiceProvider services)
    {
        _navigationView = navigationView;

        // 页面由容器解析，因此页面必须以「唯一构造函数 + 构造函数注入」声明。
        navigationView.SetServiceProvider(services);
        navigationView.Navigated += OnNavigationViewNavigated;
    }

    /// <inheritdoc />
    public void NavigateTo(ShellPage page)
    {
        if (_navigationView is null)
        {
            throw new InvalidOperationException("导航宿主尚未绑定，无法导航。");
        }

        // 返回值 false 表示「已停留在该页」或「导航被取消」，都不是错误：
        // 页面无法解析时 NavigationView 会直接抛出异常。
        _navigationView.Navigate(ShellPageRegistry.GetPageType(page));
    }

    private void OnNavigationViewNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is null || !ShellPageRegistry.TryGetShellPage(args.Page.GetType(), out var page))
        {
            return;
        }

        if (Current == page)
        {
            return;
        }

        Current = page;
        Navigated?.Invoke(this, page);
    }
}
