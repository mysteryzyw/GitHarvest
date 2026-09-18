using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 选择提交页（工作流第 2 步）。本 ticket 只放骨架占位，业务在 ticket 06、07 落地。
/// </summary>
public partial class PickCommitsPage : Page
{
    public PickCommitsPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
