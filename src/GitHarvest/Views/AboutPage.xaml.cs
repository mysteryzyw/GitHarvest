using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 关于页（工具页）。本 ticket 只放骨架占位，内容在 ticket 12 落地。
/// </summary>
public partial class AboutPage : Page
{
    public AboutPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
