using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 关于页（工具页）：应用版本、git 版本与作者信息。
/// 加载即取一次 git 自检结果（命中探测缓存），因此构造时直接执行一次加载命令。
/// </summary>
public partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        // 「关于」页没有别的入口去触发加载，构造完就取一次版本信息（探测命中缓存，不启进程）。
        Loaded += (_, _) => viewModel.LoadCommand.Execute(null);
    }
}
