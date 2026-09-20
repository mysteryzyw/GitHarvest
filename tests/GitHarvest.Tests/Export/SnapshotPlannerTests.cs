using GitHarvest.Core.Export;
using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 快照落位规则的测试（纯函数）：变更范围内的每个条目该落到「更新前 / 更新后」的哪一侧、
/// 写在哪个相对路径上。这是 spec 用户故事 24 的核心——修改两侧各一份、新增只在更新后、
/// 删除只在更新前、重命名的旧路径进更新前而新路径进更新后。
/// 子模块指针（gitlink）没有文件内容，任何一侧都不写，但它在清单里照旧可见（用户故事 28）。
/// </summary>
public sealed class SnapshotPlannerTests
{
    [Fact]
    public void 新增只落更新后()
    {
        var plan = Plan(MakeFile("src/new.cs", ChangeKind.Added, oldBlob: null, newBlob: "b1"));

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(SnapshotSide.After, entry.Side);
        Assert.Equal("src/new.cs", entry.RelativePath);
        Assert.Equal("b1", entry.BlobHash);
        Assert.Equal(0, plan.BeforeCount);
        Assert.Equal(1, plan.AfterCount);
    }

    [Fact]
    public void 删除只落更新前()
    {
        var plan = Plan(MakeFile("src/gone.cs", ChangeKind.Deleted, oldBlob: "b0", newBlob: null));

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(SnapshotSide.Before, entry.Side);
        Assert.Equal("src/gone.cs", entry.RelativePath);
        Assert.Equal("b0", entry.BlobHash);
    }

    [Fact]
    public void 修改两侧各一份且路径相同()
    {
        var plan = Plan(MakeFile("src/mod.cs", ChangeKind.Modified, oldBlob: "b0", newBlob: "b1"));

        Assert.Equal(2, plan.Entries.Count);

        var before = BeforeEntry(plan);
        Assert.Equal("src/mod.cs", before.RelativePath);
        Assert.Equal("b0", before.BlobHash);

        var after = AfterEntry(plan);
        Assert.Equal("src/mod.cs", after.RelativePath);
        Assert.Equal("b1", after.BlobHash);
    }

    [Fact]
    public void 重命名的旧路径进更新前而新路径进更新后()
    {
        var plan = Plan(MakeFile(
            "src/新名.cs",
            ChangeKind.Renamed,
            oldBlob: "b0",
            newBlob: "b1",
            oldPath: "src/旧名.cs"));

        Assert.Equal("src/旧名.cs", BeforeEntry(plan).RelativePath);
        Assert.Equal("b0", BeforeEntry(plan).BlobHash);
        Assert.Equal("src/新名.cs", AfterEntry(plan).RelativePath);
        Assert.Equal("b1", AfterEntry(plan).BlobHash);
    }

    [Theory]
    [InlineData(null, "b1")]
    [InlineData("b0", null)]
    [InlineData("b0", "b1")]
    public void 子模块指针的三种形态都不写快照(string? oldBlob, string? newBlob)
    {
        // 子模块的「blob 哈希」其实指向另一个仓库的提交，按它取内容会失败；
        // 新增、删除、换指针三种形态都没有本仓库的内容可取。
        var plan = Plan(MakeFile(
            "vendor/lib",
            ChangeKind.Other,
            oldBlob,
            newBlob,
            reason: OtherChangeReason.SubmodulePointer));

        Assert.Empty(plan.Entries);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void 子模块判定看原因而不是类型()
    {
        // 现实中的子模块条目一定被归成「其他」（ticket 08 的归类规则）；这里刻意用「新增」
        // 构造一条不现实的记录，验证判据落在「原因」上——万一将来归类改动漏掉子模块，
        // 也不会写出注定失败的内容。
        var plan = Plan(MakeFile(
            "vendor/lib",
            ChangeKind.Added,
            oldBlob: null,
            newBlob: "b1",
            reason: OtherChangeReason.SubmodulePointer));

        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void 其他类的类型变更有内容时两侧都写()
    {
        var plan = Plan(MakeFile(
            "link.txt",
            ChangeKind.Other,
            oldBlob: "b0",
            newBlob: "b1",
            reason: OtherChangeReason.TypeChange));

        Assert.Equal(2, plan.Entries.Count);
    }

    [Fact]
    public void 计划数字分别统计两侧的待写文件()
    {
        var plan = Plan(
            MakeFile("a.cs", ChangeKind.Added, null, "b1"),
            MakeFile("d.cs", ChangeKind.Deleted, "b0", null),
            MakeFile("m.cs", ChangeKind.Modified, "b0", "b1"),
            MakeFile("r.cs", ChangeKind.Renamed, "b0", "b1", oldPath: "old.cs"),
            MakeFile("vendor/lib", ChangeKind.Other, "b0", "b1", reason: OtherChangeReason.SubmodulePointer));

        // 更新前：删除 + 修改 + 重命名；更新后：新增 + 修改 + 重命名；子模块两侧都不写。
        Assert.Equal(3, plan.BeforeCount);
        Assert.Equal(3, plan.AfterCount);
        Assert.Equal(6, plan.Entries.Count);
    }

    [Fact]
    public void 空变更范围得到空计划()
    {
        var plan = SnapshotPlanner.Plan([]);

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Entries);
        Assert.Equal(0, plan.BeforeCount);
        Assert.Equal(0, plan.AfterCount);
    }

    private static SnapshotPlan Plan(params ChangedFile[] files) => SnapshotPlanner.Plan(files);

    private static SnapshotEntry BeforeEntry(SnapshotPlan plan)
        => Assert.Single(plan.Entries, entry => entry.Side == SnapshotSide.Before);

    private static SnapshotEntry AfterEntry(SnapshotPlan plan)
        => Assert.Single(plan.Entries, entry => entry.Side == SnapshotSide.After);

    private static ChangedFile MakeFile(
        string path,
        ChangeKind kind,
        string? oldBlob,
        string? newBlob,
        string? oldPath = null,
        OtherChangeReason reason = OtherChangeReason.None)
        => new(
            path,
            oldPath,
            kind,
            reason,
            Additions: 1,
            Deletions: 1,
            SimilarityPercent: null,
            SizeBytes: 10,
            oldBlob,
            newBlob);
}
