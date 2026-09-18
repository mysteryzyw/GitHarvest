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

    /// <summary>当前目的地的标题（页面标题直接取用，避免多处重复维护）。</summary>
    public string CurrentTitle => _catalog.Get(CurrentPage).Title;

    /// <summary>步骤指示条的四步。</summary>
    public IReadOnlyList<WorkflowStepStatus> Steps { get; private set; } = [];

    public bool CanGoPrevious => _catalog.GetPrevious(CurrentPage) is not null;

    public bool CanGoNext => _catalog.GetNext(CurrentPage) is not null;

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

    partial void OnCurrentPageChanged(ShellPage value) => Refresh();

    private void OnShellNavigated(object? sender, ShellPage page) => CurrentPage = page;

    private void Refresh()
    {
        Steps = _catalog.GetStepStatuses(CurrentPage);

        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}
