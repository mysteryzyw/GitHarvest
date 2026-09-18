namespace GitHarvest.Core.Navigation;

/// <summary>
/// 外壳导航目录：四步工作流的顺序与标题、并列工具页，以及步进语义。
/// ViewModel 只依赖本接口，不依赖任何界面类型。
/// </summary>
public interface INavigationCatalog
{
    /// <summary>四步工作流，按顺序排列。</summary>
    IReadOnlyList<NavigationItem> WorkflowSteps { get; }

    /// <summary>与工作流并列的工具页（全局设置、关于）。</summary>
    IReadOnlyList<NavigationItem> UtilityPages { get; }

    /// <summary>全部目的地：四步工作流在前，工具页在后。</summary>
    IReadOnlyList<NavigationItem> All { get; }

    /// <summary>查询目的地对应的条目；未知目的地抛出异常。</summary>
    NavigationItem Get(ShellPage page);

    /// <summary>工作流中的下一步；已是最后一步或工具页时返回 <see langword="null"/>。</summary>
    NavigationItem? GetNext(ShellPage page);

    /// <summary>工作流中的上一步；已是第一步或工具页时返回 <see langword="null"/>。</summary>
    NavigationItem? GetPrevious(ShellPage page);

    /// <summary>是否为四步工作流中的一步。</summary>
    bool IsWorkflowStep(ShellPage page);

    /// <summary>
    /// 步骤指示条的四步状态：当前目的地之前的步骤为已完成、当前步为进行中、之后的为未到达；
    /// 目的地是工具页（全局设置 / 关于）时四步一律为未到达。
    /// </summary>
    IReadOnlyList<WorkflowStepStatus> GetStepStatuses(ShellPage current);
}
