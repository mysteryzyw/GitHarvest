namespace GitHarvest.Core.Navigation;

/// <summary>
/// 切换外壳导航目的地。界面框架相关的实现由 WPF 壳提供，ViewModel 只依赖本接口。
/// </summary>
public interface IShellNavigator
{
    /// <summary>当前目的地。默认是工作流的第一步。</summary>
    ShellPage Current { get; }

    /// <summary>目的地发生变化时触发（无论由导航项点击还是由 ViewModel 触发）。</summary>
    event EventHandler<ShellPage>? Navigated;

    /// <summary>切换到指定目的地。</summary>
    void NavigateTo(ShellPage page);
}
