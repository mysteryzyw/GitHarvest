using System.Windows.Controls;

namespace GitHarvest.Controls;

/// <summary>
/// 工作流页脚：上一步 / 下一步。命令与可用性来自页面级 <c>ShellViewModel</c>。
/// </summary>
public partial class WorkflowFooter : UserControl
{
    public WorkflowFooter() => InitializeComponent();
}
