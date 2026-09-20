using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 导出前总预览统计汇总的单元测试：按变更类型的计数、占比取整与空范围判断。
/// </summary>
public sealed class ChangeRangeSummaryTests
{
    [Fact]
    public void 按类型计数与占比()
    {
        var summary = new ChangeRangeSummary(
        [
            Make(ChangeKind.Added),
            Make(ChangeKind.Added),
            Make(ChangeKind.Added),
            Make(ChangeKind.Deleted),
            Make(ChangeKind.Deleted),
            Make(ChangeKind.Modified),
            Make(ChangeKind.Renamed),
            Make(ChangeKind.Other),
        ]);

        Assert.Equal(8, summary.TotalCount);
        Assert.False(summary.IsEmpty);
        Assert.Equal(3, summary.CountOf(ChangeKind.Added));
        Assert.Equal(2, summary.CountOf(ChangeKind.Deleted));
        Assert.Equal(1, summary.CountOf(ChangeKind.Modified));
        Assert.Equal(1, summary.CountOf(ChangeKind.Renamed));
        Assert.Equal(1, summary.CountOf(ChangeKind.Other));
        // 3/8=37.5、2/8=25、1/8=12.5——四舍五入到整数（原型 toFixed(0)）。
        Assert.Equal(38, summary.PercentOf(ChangeKind.Added));
        Assert.Equal(25, summary.PercentOf(ChangeKind.Deleted));
        Assert.Equal(13, summary.PercentOf(ChangeKind.Modified));
    }

    [Fact]
    public void 空范围时全部计数与占比为零()
    {
        var summary = new ChangeRangeSummary([]);

        Assert.True(summary.IsEmpty);
        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.CountOf(ChangeKind.Added));
        Assert.Equal(0, summary.PercentOf(ChangeKind.Added));
    }

    [Fact]
    public void 按类型取文件清单()
    {
        var added = Make(ChangeKind.Added);
        var modified = Make(ChangeKind.Modified);
        var summary = new ChangeRangeSummary([added, modified, Make(ChangeKind.Added)]);

        Assert.Equal([added, summary.Files[2]], summary.FilesOf(ChangeKind.Added));
        Assert.Equal([modified], summary.FilesOf(ChangeKind.Modified));
        Assert.Empty(summary.FilesOf(ChangeKind.Deleted));
    }

    private static ChangedFile Make(ChangeKind kind)
        => new($"f-{kind}-{Guid.NewGuid():N}", null, kind, OtherChangeReason.None, 1, 1, null, 10, null, null);
}
