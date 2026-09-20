using GitHarvest.Core.Export;
using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 默认更新说明内容的测试（纯函数）：本 ticket 先用自动生成的内容，
/// 编辑器与 12 个占位符由 ticket 10 接管。这里锁定 spec 用户故事 36 要求的要素——
/// 更新日期、分支名、基准/Head 哈希与提交信息、按变更类型分组的文件清单与变更统计，
/// 以及三个可手写栏目；用户故事 29 要求二进制文件在说明里标注「二进制」。
/// </summary>
public sealed class UpdateNotesComposerTests
{
    [Fact]
    public void 说明含范围信息与手写栏目()
    {
        var notes = Compose(range: [MakeFile("a.cs", ChangeKind.Added)]);

        Assert.Contains("# 更新说明", notes, StringComparison.Ordinal);
        Assert.Contains("2026-09-20", notes, StringComparison.Ordinal);
        Assert.Contains("main", notes, StringComparison.Ordinal);
        Assert.Contains("a1b2c3d", notes, StringComparison.Ordinal);
        Assert.Contains("chore(release): 2.4.0 版本冻结", notes, StringComparison.Ordinal);
        Assert.Contains("e4f5g6h", notes, StringComparison.Ordinal);
        Assert.Contains("feat(export): 支持更新说明模板占位符", notes, StringComparison.Ordinal);

        // 三个可手写栏目（用户故事 36）——留空让使用者填写，而不是省略小节。
        Assert.Contains("更新内容说明", notes, StringComparison.Ordinal);
        Assert.Contains("操作步骤", notes, StringComparison.Ordinal);
        Assert.Contains("注意事项", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 变更统计按类型给出文件数与占比()
    {
        var notes = Compose(range:
        [
            MakeFile("a1.cs", ChangeKind.Added),
            MakeFile("a2.cs", ChangeKind.Added),
            MakeFile("d.cs", ChangeKind.Deleted),
            MakeFile("m.cs", ChangeKind.Modified),
            MakeFile("m2.cs", ChangeKind.Modified),
        ]);

        // 共 5 个文件：新增 2（40%）、删除 1（20%）、修改 2（40%）。
        Assert.Contains("共 5 个文件", notes, StringComparison.Ordinal);
        Assert.Contains("| 新增 | 2 | 40% |", notes, StringComparison.Ordinal);
        Assert.Contains("| 删除 | 1 | 20% |", notes, StringComparison.Ordinal);
        Assert.Contains("| 修改 | 2 | 40% |", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 按类型分组列出文件清单且空组不出现()
    {
        var notes = Compose(range:
        [
            MakeFile("src/新增.cs", ChangeKind.Added),
            MakeFile("src/删除.cs", ChangeKind.Deleted),
            MakeFile("src/保持.cs", ChangeKind.Modified),
        ]);

        Assert.Contains("### 新增（1）", notes, StringComparison.Ordinal);
        Assert.Contains("### 删除（1）", notes, StringComparison.Ordinal);
        Assert.Contains("### 修改（1）", notes, StringComparison.Ordinal);
        Assert.Contains("src/新增.cs", notes, StringComparison.Ordinal);

        // 重命名与其他两类一个文件都没有，不该出现空小节。
        Assert.DoesNotContain("### 重命名", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("### 其他", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 重命名列出旧路径到新路径与相似度()
    {
        var notes = Compose(range:
        [
            new ChangedFile(
                "src/新名.cs",
                "src/旧名.cs",
                ChangeKind.Renamed,
                OtherChangeReason.None,
                Additions: 1,
                Deletions: 1,
                SimilarityPercent: 92,
                SizeBytes: 10,
                OldBlobHash: "b0",
                NewBlobHash: "b1"),
        ]);

        Assert.Contains("src/旧名.cs", notes, StringComparison.Ordinal);
        Assert.Contains("src/新名.cs", notes, StringComparison.Ordinal);
        Assert.Contains("92%", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 其他类标注具体原因()
    {
        var notes = Compose(range:
        [
            new ChangedFile("vendor/lib", null, ChangeKind.Other, OtherChangeReason.SubmodulePointer, 1, 1, null, null, "b0", "b1"),
            new ChangedFile("link.txt", null, ChangeKind.Other, OtherChangeReason.TypeChange, 1, 1, null, 10, "b0", "b1"),
        ]);

        Assert.Contains("子模块指针", notes, StringComparison.Ordinal);
        Assert.Contains("类型变更", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 二进制文件在清单里标注()
    {
        var notes = Compose(range:
        [
            // 二进制：git numstat 两侧都以 "-" 占位（行数为空）。
            new ChangedFile("logo.bin", null, ChangeKind.Added, OtherChangeReason.None, null, null, null, 11, null, "b1"),
        ]);

        Assert.Contains("logo.bin", notes, StringComparison.Ordinal);
        Assert.Contains("二进制", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 二进制重命名同样标注二进制()
    {
        // 用户故事 29 的组合场景：重命名的二进制文件既要有「旧 → 新」与相似度，
        // 也要标注「二进制」——两个标注并存，不能让相似度把「二进制」挤掉。
        var notes = Compose(range:
        [
            new ChangedFile(
                "assets/logo-new.bin",
                "assets/logo-old.bin",
                ChangeKind.Renamed,
                OtherChangeReason.None,
                Additions: null,
                Deletions: null,
                SimilarityPercent: 88,
                SizeBytes: 11,
                OldBlobHash: "b0",
                NewBlobHash: "b1"),
        ]);

        Assert.Contains("assets/logo-old.bin", notes, StringComparison.Ordinal);
        Assert.Contains("assets/logo-new.bin", notes, StringComparison.Ordinal);
        Assert.Contains("88%", notes, StringComparison.Ordinal);
        Assert.Contains("二进制", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新日期取实际使用的目录名()
    {
        // 目录名被重名让位成带时分的形态时，说明里的更新日期与实际目录一致。
        var notes = Compose(folderName: "2026-09-20_134500", range: [MakeFile("a.cs", ChangeKind.Added)]);

        Assert.Contains("2026-09-20_134500", notes, StringComparison.Ordinal);
    }

    private static string Compose(
        IReadOnlyList<ChangedFile> range,
        string folderName = "2026-09-20")
        => UpdateNotesComposer.Compose(
            new ExportRequest(
                RepositoryPath: @"D:\Code\DemoService",
                OutputRootPath: @"D:\交付物\DemoService",
                FolderName: folderName,
                RequestedAt: new DateTimeOffset(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8)),
                BranchName: "main",
                Base: new CommitSummary("a1b2c3d", "chore(release): 2.4.0 版本冻结", "王磊", DateTimeOffset.Now),
                Head: new CommitSummary("e4f5g6h", "feat(export): 支持更新说明模板占位符", "张伟", DateTimeOffset.Now)),
            new ChangeRangeSummary(range));

    private static ChangedFile MakeFile(string path, ChangeKind kind)
        => new(path, null, kind, OtherChangeReason.None, Additions: 1, Deletions: 0, SimilarityPercent: null, SizeBytes: 10, "b0", "b1");
}
