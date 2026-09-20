using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Navigation;

namespace GitHarvest.ViewModels;

/// <summary>
/// 外壳状态：当前目的地、步骤指示条与「上一步 / 下一步」。
/// 只依赖 Core 的 <see cref="INavigationCatalog"/> 与 <see cref="IShellNavigator"/>，
/// 不接触任何界面类型、进程或文件系统。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationCatalog _catalog;
    private readonly IShellNavigator _navigator;

    [ObservableProperty]
    private ShellPage _currentPage = ShellPage.Repository;

    public ShellViewModel(INavigationCatalog catalog, IShellNavigator navigator)
    {
        _catalog = catalog;
        _navigator = navigator;
        _navigator.Navigated += OnShellNavigated;

        Refresh();
    }

    /// <summary>当前目的地的页面标题（首屏是欢迎语，其余与导航标题相同，由导航目录统一维护）。</summary>
    public string CurrentTitle => _catalog.Get(CurrentPage).PageTitleOrDefault;

    /// <summary>步骤指示条的四步。</summary>
    public IReadOnlyList<WorkflowStepStatus> Steps { get; private set; } = [];

    public bool CanGoPrevious => _catalog.GetPrevious(CurrentPage) is not null;

    /// <summary>
    /// 「下一步」的可达性：导航目录里有下一站，且当前页面没有用业务校验结论挡住它。
    /// 以第 2 步「选择提交」为例，两种情况会挡：基准或 Head 未选齐，以及校验跑成的结论是
    /// 「不是祖先」（分叉提交对）。校验流程本身失败（分支被删、git 不可用）只给中性提示、不挡。
    /// </summary>
    public bool CanGoNext => !PageBlocksNext && _catalog.GetNext(CurrentPage) is not null;

    /// <summary>
    /// 当前页面是否用业务校验结论挡住了「下一步」。页面 VM 置位 / 复位；
    /// 换页时由壳复位——旧页面的校验结论不带到新页，新页面按自身状态重新决定。
    /// </summary>
    [ObservableProperty]
    private bool _pageBlocksNext;

    partial void OnPageBlocksNextChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    /// <summary>
    /// 「下一步」按钮的文案。原型把下一步的目的地写进按钮里（「下一步：导出前总预览 →」），
    /// 这里同样从导航目录取标题；走到工作流最后一步时按原型显示「开始导出」
    /// （导出动作在 ticket 10 落地，按钮届时才可用）。
    /// </summary>
    public string NextButtonText =>
        _catalog.GetNext(CurrentPage) is { } next
            ? $"下一步：{next.Title} →"
            : _catalog.IsWorkflowStep(CurrentPage) ? "开始导出" : "下一步";

    [RelayCommand]
    private void GoPrevious()
    {
        if (_catalog.GetPrevious(CurrentPage) is { } previous)
        {
            _navigator.NavigateTo(previous.Page);
        }
    }

    [RelayCommand]
    private void GoNext()
    {
        if (_catalog.GetNext(CurrentPage) is { } next)
        {
            _navigator.NavigateTo(next.Page);
        }
    }

    partial void OnCurrentPageChanged(ShellPage value)
    {
        // 换页即复位业务门控：旧页面的「校验未通过」结论对新页面没有意义，
        // 新页面（Transient VM）加载后会按自身状态重新置位。
        PageBlocksNext = false;
        Refresh();
    }

    private void OnShellNavigated(object? sender, ShellPage page) => CurrentPage = page;

    private void Refresh()
    {
        Steps = _catalog.GetStepStatuses(CurrentPage);

        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
    }
}
