namespace GitHarvest.Core.Navigation;

/// <summary>
/// 导航目录中的一项：目的地、显示标题、性质与工作流序号。
/// 原型里同一目的地最多有三套文案：左侧导航（最短）、步骤指示条（首尾两步更长）、
/// 页面标题（首屏是欢迎语），分别由 <see cref="Title"/>、<see cref="StepTitle"/>、<see cref="PageTitle"/> 承载。
/// </summary>
/// <param name="Page">目的地。</param>
/// <param name="Title">左侧导航使用的中文标题。</param>
/// <param name="Kind">工作流步骤或工具页。</param>
/// <param name="Ordinal">工作流步骤的序号（1..4）；工具页为 <see langword="null"/>。</param>
/// <param name="StepTitle">步骤指示条使用的标题；<see langword="null"/> 时回落到 <paramref name="Title"/>。</param>
/// <param name="PageTitle">页面标题；<see langword="null"/> 时回落到 <paramref name="Title"/>。</param>
public sealed record NavigationItem(
    ShellPage Page,
    string Title,
    ShellPageKind Kind,
    int? Ordinal,
    string? StepTitle = null,
    string? PageTitle = null)
{
    /// <summary>是否为四步工作流中的一步。</summary>
    public bool IsWorkflowStep => Kind == ShellPageKind.WorkflowStep;

    /// <summary>步骤指示条标题（未单独指定时与导航标题相同）。</summary>
    public string StepTitleOrDefault => StepTitle ?? Title;

    /// <summary>页面标题（未单独指定时与导航标题相同）。</summary>
    public string PageTitleOrDefault => PageTitle ?? Title;
}
