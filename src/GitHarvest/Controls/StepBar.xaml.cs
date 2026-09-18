using System.Windows.Controls;

namespace GitHarvest.Controls;

/// <summary>
/// 步骤指示条：显示四步工作流并标出当前步骤。
/// 数据上下文由所在页面提供（页面级 <c>ShellViewModel</c>）。
/// </summary>
public partial class StepBar : UserControl
{
    public StepBar() => InitializeComponent();
}
