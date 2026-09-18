using System.IO;
using GitHarvest.Core.Interaction;
using Microsoft.Win32;

namespace GitHarvest.Shell;

/// <summary>
/// <see cref="IFolderPicker"/> 的 WPF 实现：用 Win32 文件夹对话框让用户选目录。
/// 初始目录不存在时静默忽略（比如仓库已被删除的场景），由系统决定起点。
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    /// <inheritdoc />
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Git 仓库文件夹",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
