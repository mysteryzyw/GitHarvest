namespace GitHarvest.Core.Navigation;

/// <summary>导航目的地的性质。</summary>
public enum ShellPageKind
{
    /// <summary>四步工作流中的一步，带 1..4 的序号。</summary>
    WorkflowStep = 1,

    /// <summary>与工作流并列的工具页，无序号、不参与工作流步进。</summary>
    Utility = 2,
}
