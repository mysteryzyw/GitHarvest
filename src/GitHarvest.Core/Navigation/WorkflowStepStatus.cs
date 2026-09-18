namespace GitHarvest.Core.Navigation;

/// <summary>
/// 步骤指示条中的一步：序号、标题与状态。标题与序号来自导航目录，界面只负责渲染。
/// </summary>
/// <param name="Ordinal">步骤序号，1..4。</param>
/// <param name="Title">步骤标题。</param>
/// <param name="State">该步在给定目的地下的状态。</param>
public sealed record WorkflowStepStatus(int Ordinal, string Title, WorkflowStepState State);
