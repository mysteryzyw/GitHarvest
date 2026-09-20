using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 导出前总预览页（工作流第 3 步）：页面加载时读取会话里的变更范围并自动计算
/// （无范围时停在引导卡）；「重新计算」由页面命令承担。
/// </summary>
public partial class PreviewPage : Page
{
    private readonly PreviewViewModel _viewModel;

    public PreviewPage(PreviewViewModel viewModel)
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
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
