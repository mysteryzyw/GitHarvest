using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 更新说明页（工作流第 4 步，ticket 09）：页面加载时经 Core 的导出预检拿到更新包结构与
/// 文件系统冲突清单（按下导出前就给出反馈）；页脚的「开始导出」由本页 VM 接管。
/// </summary>
public partial class NotesPage : Page
{
    private readonly NotesViewModel _viewModel;

    public NotesPage(NotesViewModel viewModel)
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
