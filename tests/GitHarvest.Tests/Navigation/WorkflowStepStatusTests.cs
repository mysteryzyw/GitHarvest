using GitHarvest.Core.Navigation;

namespace GitHarvest.Tests.Navigation;

/// <summary>
/// 步骤指示条状态的外部行为：四步工作流中每一步在任意目的地下的状态。
/// </summary>
public class WorkflowStepStatusTests
{
    private static readonly NavigationCatalog Catalog = new();

    [Fact]
    public void 第一步时后续步骤均为未到达()
    {
        var statuses = Catalog.GetStepStatuses(ShellPage.Repository);

        Assert.Equal(
            new[]
            {
                WorkflowStepState.Current,
                WorkflowStepState.Pending,
                WorkflowStepState.Pending,
                WorkflowStepState.Pending,
            },
            statuses.Select(status => status.State));
    }

    [Fact]
    public void 中间步骤时之前的步骤为已完成之后的为未到达()
    {
        var statuses = Catalog.GetStepStatuses(ShellPage.Preview);

        Assert.Equal(
            new[]
            {
                WorkflowStepState.Done,
                WorkflowStepState.Done,
                WorkflowStepState.Current,
                WorkflowStepState.Pending,
            },
            statuses.Select(status => status.State));
    }

    [Fact]
    public void 最后一步时之前的步骤全部为已完成()
    {
        var statuses = Catalog.GetStepStatuses(ShellPage.Notes);

        Assert.Equal(
            new[]
            {
                WorkflowStepState.Done,
                WorkflowStepState.Done,
                WorkflowStepState.Done,
                WorkflowStepState.Current,
            },
            statuses.Select(status => status.State));
    }

    [Theory]
    [InlineData(ShellPage.Settings)]
    [InlineData(ShellPage.About)]
    public void 工具页上四步均视为未到达(ShellPage utilityPage)
    {
        var statuses = Catalog.GetStepStatuses(utilityPage);

        Assert.All(statuses, status => Assert.Equal(WorkflowStepState.Pending, status.State));
    }

    [Fact]
    public void 每一步都带有序号与标题且与工作流一致()
    {
        var statuses = Catalog.GetStepStatuses(ShellPage.Repository);

        Assert.Equal(Catalog.WorkflowSteps.Count, statuses.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, statuses.Select(status => status.Ordinal));
        Assert.Equal(
            Catalog.WorkflowSteps.Select(step => step.Title),
            statuses.Select(status => status.Title));
    }

    [Fact]
    public void 未知目的地会明确失败()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.GetStepStatuses((ShellPage)999));
    }
}
