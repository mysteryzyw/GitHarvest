using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 导出前总预览页（工作流第 3 步）。本 ticket 只放骨架占位，业务在 ticket 08 落地。
/// </summary>
public partial class PreviewPage : Page
{
    public PreviewPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
