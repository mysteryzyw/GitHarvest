using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 选择提交页（工作流第 2 步）：页面加载时读取分支列表；
/// 分支项的点击除了触发选中命令，还要负责把下拉收起（StaysOpen=False 只响应点外部）。
/// </summary>
public partial class PickCommitsPage : Page
{
    private readonly PickCommitsViewModel _viewModel;

    public PickCommitsPage(PickCommitsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面由导航每次新建（Transient），Loaded 只会来一次；显式退订防重复加载。
        Loaded -= OnLoaded;
        await _viewModel.LoadBranchesCommand.ExecuteAsync(null);
    }

    /// <summary>分支项点击：选中由 Command 完成，这里把下拉收起来。</summary>
    private void OnBranchItemClick(object sender, RoutedEventArgs e)
        => BranchComboButton.IsChecked = false;
}
