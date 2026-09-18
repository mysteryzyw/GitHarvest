using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 全局设置页（工具页）。本 ticket 只放骨架占位，业务在 ticket 12 落地。
/// </summary>
public partial class SettingsPage : Page
{
    public SettingsPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
