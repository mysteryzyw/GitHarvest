using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 全局设置页。保存口径是即时保存：文本框在**失焦**时提交（回车走输入框自己的 KeyBinding），
/// 因此这里只需把失焦转成对应命令——「什么时候算改完了」是光标行为，属于视图层。
/// </summary>
public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    /// <summary>默认输出路径失焦即保存。</summary>
    private void OnOutputPathLostFocus(object sender, RoutedEventArgs e)
        => ViewModel.CommitOutputPathCommand.Execute(null);

    /// <summary>默认说明模板路径失焦即保存。</summary>
    private void OnTemplatePathLostFocus(object sender, RoutedEventArgs e)
        => ViewModel.CommitTemplatePathCommand.Execute(null);

    /// <summary>git.exe 路径失焦即保存并复验（<c>git --version</c>）。</summary>
    private void OnGitExecutableLostFocus(object sender, RoutedEventArgs e)
        => ViewModel.CommitGitExecutableCommand.Execute(null);

    /// <summary>数据保留天数失焦即保存，并立即按新天数清理一次。</summary>
    private void OnRetentionDaysLostFocus(object sender, RoutedEventArgs e)
        => ViewModel.CommitDataRetentionDaysCommand.Execute(null);
}
