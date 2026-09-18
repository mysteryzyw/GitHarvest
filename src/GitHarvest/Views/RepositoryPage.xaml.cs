using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 仓库页（工作流第 1 步）。本 ticket 只放骨架占位，业务在 ticket 04 落地。
/// </summary>
public partial class RepositoryPage : Page
{
    public RepositoryPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
