using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 更新说明页（工作流第 4 步）。本 ticket 只放骨架占位，业务在 ticket 09、10 落地。
/// </summary>
public partial class NotesPage : Page
{
    public NotesPage(ShellViewModel shellViewModel)
    {
        DataContext = shellViewModel;
        InitializeComponent();
    }
}
