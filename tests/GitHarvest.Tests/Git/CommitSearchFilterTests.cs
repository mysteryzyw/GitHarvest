using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 提交搜索匹配的纯函数测试：不需要真实仓库——匹配规则是「哈希 / 信息首行 / 作者任一命中、
/// 大小写不敏感」，是界面搜索框与 Core 内存过滤共用的唯一实现。
/// </summary>
public sealed class CommitSearchFilterTests
{
    private static readonly CommitSummary Sample = new(
        ShortHash: "a1b2c3d",
        Subject: "fix(order): 修复订单导出缺陷",
        AuthorName: "王磊",
        AuthorTime: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(8)));

    [Fact]
    public void 词命中短哈希时匹配()
    {
        Assert.True(CommitSearchFilter.Matches(Sample, "a1b2c3d"));
        Assert.True(CommitSearchFilter.Matches(Sample, "b2c3"));
    }

    [Fact]
    public void 词命中提交信息时匹配()
    {
        Assert.True(CommitSearchFilter.Matches(Sample, "订单导出"));
        Assert.True(CommitSearchFilter.Matches(Sample, "fix(order)"));
    }

    [Fact]
    public void 词命中作者时匹配()
    {
        Assert.True(CommitSearchFilter.Matches(Sample, "王磊"));
    }

    [Fact]
    public void 英文大小写不敏感()
    {
        Assert.True(CommitSearchFilter.Matches(Sample, "FIX(ORDER)"));
        Assert.True(CommitSearchFilter.Matches(Sample, "A1B2C3D"));
    }

    [Fact]
    public void 三字段都不命中时不匹配()
    {
        Assert.False(CommitSearchFilter.Matches(Sample, "仓库里不存在的词"));
    }

    [Fact]
    public void 空白词视为不过滤()
    {
        Assert.True(CommitSearchFilter.Matches(Sample, "   "));
        Assert.True(CommitSearchFilter.Matches(Sample, ""));
    }

    [Fact]
    public void Filter按新提交在前的顺序保留命中项()
    {
        var commits = new List<CommitSummary>
        {
            Sample,
            Sample with { ShortHash = "deadbeef", Subject = "chore: 无关提交", AuthorName = "李娜" },
            Sample with { ShortHash = "feedface", Subject = "feat: 订单导出字段补全", AuthorName = "李娜" },
        };

        var matched = CommitSearchFilter.Filter(commits, "订单导出");

        Assert.Equal(["a1b2c3d", "feedface"], matched.Select(c => c.ShortHash));
    }

    [Fact]
    public void Filter空白词返回全部()
    {
        var commits = new List<CommitSummary> { Sample, Sample with { ShortHash = "deadbeef" } };

        Assert.Equal(2, CommitSearchFilter.Filter(commits, "  ").Count);
    }
}
