using GitHarvest.Core.Navigation;

namespace GitHarvest.Tests.Navigation;

/// <summary>
/// 外壳导航目录的外部行为：四步工作流的内容、顺序与步进语义。
/// </summary>
public class NavigationCatalogTests
{
    private static readonly NavigationCatalog Catalog = new();

    private static IEnumerable<ShellPage> WorkflowStepPages =>
        [ShellPage.Repository, ShellPage.PickCommits, ShellPage.Preview, ShellPage.Notes];

    [Fact]
    public void 工作流由四步组成且顺序固定()
    {
        var steps = Catalog.WorkflowSteps;

        Assert.Equal(
            new[] { ShellPage.Repository, ShellPage.PickCommits, ShellPage.Preview, ShellPage.Notes },
            steps.Select(step => step.Page));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, steps.Select(step => step.Ordinal));
    }

    [Fact]
    public void 四步工作流的中文标题与原型一致()
    {
        Assert.Equal(
            new[] { "仓库", "选择提交", "导出前总预览", "更新说明" },
            Catalog.WorkflowSteps.Select(step => step.Title));
    }

    [Fact]
    public void 全局设置与关于是并列的工具页不属于工作流()
    {
        Assert.Equal(
            new[] { ShellPage.Settings, ShellPage.About },
            Catalog.UtilityPages.Select(page => page.Page));
        Assert.Equal(new[] { "全局设置", "关于" }, Catalog.UtilityPages.Select(page => page.Title));

        Assert.All(Catalog.UtilityPages, page =>
        {
            Assert.Equal(ShellPageKind.Utility, page.Kind);
            Assert.Null(page.Ordinal);
        });
    }

    [Fact]
    public void 全部目的地包含四步与两个工具页且标题不重复()
    {
        var all = Catalog.All;

        Assert.Equal(6, all.Count);
        Assert.All(all, item => Assert.False(string.IsNullOrWhiteSpace(item.Title)));
        Assert.Equal(all.Count, all.Select(item => item.Title).Distinct().Count());
        Assert.Equal(all.Count, all.Select(item => item.Page).Distinct().Count());
    }

    [Fact]
    public void 可按目的地查询条目()
    {
        Assert.Equal("选择提交", Catalog.Get(ShellPage.PickCommits).Title);
        Assert.Equal(ShellPageKind.WorkflowStep, Catalog.Get(ShellPage.PickCommits).Kind);
        Assert.Equal("关于", Catalog.Get(ShellPage.About).Title);
    }

    [Fact]
    public void 未知目的地查询会明确失败()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.Get((ShellPage)999));
    }

    [Theory]
    [InlineData(ShellPage.Repository, ShellPage.PickCommits)]
    [InlineData(ShellPage.PickCommits, ShellPage.Preview)]
    [InlineData(ShellPage.Preview, ShellPage.Notes)]
    public void 工作流中间步骤有前后相邻步骤(ShellPage current, ShellPage expectedNext)
    {
        Assert.Equal(expectedNext, Catalog.GetNext(current)!.Page);
        Assert.Equal(current, Catalog.GetPrevious(expectedNext)!.Page);
    }

    [Fact]
    public void 工作流首尾步骤的步进在边界处结束()
    {
        Assert.Null(Catalog.GetPrevious(ShellPage.Repository));
        Assert.Null(Catalog.GetNext(ShellPage.Notes));
    }

    [Fact]
    public void 工具页不参与工作流步进()
    {
        Assert.Null(Catalog.GetNext(ShellPage.Settings));
        Assert.Null(Catalog.GetPrevious(ShellPage.Settings));
        Assert.Null(Catalog.GetNext(ShellPage.About));
        Assert.Null(Catalog.GetPrevious(ShellPage.About));
    }

    [Fact]
    public void 可判断目的地是否为工作流步骤()
    {
        Assert.All(WorkflowStepPages, page => Assert.True(Catalog.IsWorkflowStep(page)));
        Assert.False(Catalog.IsWorkflowStep(ShellPage.Settings));
        Assert.False(Catalog.IsWorkflowStep(ShellPage.About));
    }
}
