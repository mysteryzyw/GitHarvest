using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 仓库页（工作流第 1 步）。数据上下文是 <see cref="RepositoryViewModel"/>（外壳状态经它的 Shell 属性取），
/// 页面加载时触发一次 git 环境自检的读取——服务内部缓存结果，因此每次导航回来都不会重新启动进程。
/// </summary>
public partial class RepositoryPage : Page
{
    private readonly RepositoryViewModel _viewModel;

    public RepositoryPage(RepositoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 命令内部已把异常收敛成「不可用」状态，这里不需要额外的错误处理。
        _viewModel.CheckGitEnvironmentCommand.Execute(null);
    }
}
