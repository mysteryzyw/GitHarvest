namespace GitHarvest.Core.Navigation;

/// <summary>步骤指示条中某一步的状态。</summary>
public enum WorkflowStepState
{
    /// <summary>尚未到达。</summary>
    Pending = 0,

    /// <summary>当前所在步骤。</summary>
    Current = 1,

    /// <summary>已完成。</summary>
    Done = 2,
}
