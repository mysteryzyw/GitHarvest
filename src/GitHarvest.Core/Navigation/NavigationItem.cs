namespace GitHarvest.Core.Navigation;

/// <summary>
/// 导航目录中的一项：目的地、显示标题、性质与工作流序号。
/// </summary>
/// <param name="Page">目的地。</param>
/// <param name="Title">左侧导航与步骤指示条使用的中文标题。</param>
/// <param name="Kind">工作流步骤或工具页。</param>
/// <param name="Ordinal">工作流步骤的序号（1..4）；工具页为 <see langword="null"/>。</param>
public sealed record NavigationItem(ShellPage Page, string Title, ShellPageKind Kind, int? Ordinal)
{
    /// <summary>是否为四步工作流中的一步。</summary>
    public bool IsWorkflowStep => Kind == ShellPageKind.WorkflowStep;
}
