using GitHarvest.Core.Export;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 文件系统冲突扫描的测试（纯函数，不碰文件系统）：spec 用户故事 32 要求导出前扫描
/// Windows 文件系统冲突（非法字符、超长路径、仅大小写不同的同名文件），
/// 发现问题即中止导出并列出**完整**问题清单——交付包绝不与仓库不一致。
/// 扫描的输入是「即将写出的路径」，因此与写出用的是同一份路径计算（见 UpdatePackageLayout）。
/// </summary>
public sealed class FileSystemConflictScannerTests
{
    private const string PackagePath = @"D:\交付物\DemoRepo\2026-09-20";

    [Fact]
    public void 常规路径没有冲突()
    {
        var conflicts = Scan(SnapshotSide.After, "src/Services/OrderService.cs", "模块/配置.json", "a b.md");

        Assert.Empty(conflicts);
    }

    [Fact]
    public void 非法字符被列出并说明是哪个字符()
    {
        var conflicts = Scan(SnapshotSide.After, "src/a:b.cs");

        var conflict = Assert.Single(conflicts);
        Assert.Equal(FileSystemConflictKind.IllegalCharacter, conflict.Kind);
        Assert.Contains(":", conflict.Description, StringComparison.Ordinal);
        Assert.Equal(["src/a:b.cs"], conflict.RelativePaths);
    }

    [Fact]
    public void 保留设备名被列出()
    {
        var conflicts = Scan(SnapshotSide.Before, "src/CON.txt", "nul");

        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, conflict => Assert.Equal(FileSystemConflictKind.ReservedName, conflict.Kind));
    }

    [Fact]
    public void 目录段以点或空格结尾被列出()
    {
        var conflicts = Scan(SnapshotSide.After, "src.v2/a.", "trailing/ ");

        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, conflict => Assert.Equal(FileSystemConflictKind.TrailingDotOrSpace, conflict.Kind));
    }

    [Fact]
    public void 完整路径达到Windows经典上限时被列出()
    {
        // MAX_PATH = 260（含结尾空字符），可用长度是 259：正好 260 视为冲突，259 不冲突。
        var pathAtLimit = RelativePathOfFullLength(FileSystemConflictScanner.MaxPathLength);
        var pathUnderLimit = RelativePathOfFullLength(FileSystemConflictScanner.MaxPathLength - 1);

        var conflicts = Scan(SnapshotSide.After, pathAtLimit, pathUnderLimit);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(FileSystemConflictKind.PathTooLong, conflict.Kind);
        Assert.Equal([pathAtLimit], conflict.RelativePaths);
    }

    [Fact]
    public void 仅大小写不同的同名文件被列出且成对出现()
    {
        var conflicts = Scan(SnapshotSide.After, "src/Order.cs", "src/order.cs", "other.cs");

        var conflict = Assert.Single(conflicts);
        Assert.Equal(FileSystemConflictKind.CaseCollision, conflict.Kind);
        Assert.Equal(["src/Order.cs", "src/order.cs"], conflict.RelativePaths);
    }

    [Fact]
    public void 大小写差异出现在不同侧时不算冲突()
    {
        // 「更新前」与「更新后」是两个独立文件夹（如重命名 Order.cs → order.cs），互不覆盖。
        var plan = SnapshotPlanner.Plan(
        [
            File("order.cs", oldPath: "Order.cs"),
        ]);

        var conflicts = FileSystemConflictScanner.Scan(plan, PackagePath);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void 多种冲突一次列全()
    {
        var conflicts = Scan(SnapshotSide.After, "src/a:b.cs", "src/CON.txt", "x/y.cs", "x/Y.cs");

        Assert.Equal(3, conflicts.Count);
        Assert.Contains(conflicts, conflict => conflict.Kind == FileSystemConflictKind.IllegalCharacter);
        Assert.Contains(conflicts, conflict => conflict.Kind == FileSystemConflictKind.ReservedName);
        Assert.Contains(conflicts, conflict => conflict.Kind == FileSystemConflictKind.CaseCollision);
    }

    [Fact]
    public void 两侧都涉及的同一条路径只列一次()
    {
        // 修改类的文件两侧都写，坏路径会被扫两遍；清单里只说明一次（名字来自仓库，修法相同）。
        var plan = SnapshotPlanner.Plan([File("src/a:b.cs")]);

        var conflicts = FileSystemConflictScanner.Scan(plan, PackagePath);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("src/a:b.cs", Assert.Single(conflict.RelativePaths));
    }

    private static IReadOnlyList<FileSystemConflict> Scan(SnapshotSide side, params string[] relativePaths)
        => FileSystemConflictScanner.Scan(side, relativePaths, PackagePath);

    /// <summary>构造一个相对路径，使它在指定侧的完整路径长度正好等于 <paramref name="fullLength"/>。</summary>
    private static string RelativePathOfFullLength(int fullLength)
    {
        var folderLength = UpdatePackageLayout.FolderPath(PackagePath, SnapshotSide.After).Length + 1;
        var nameLength = fullLength - folderLength;

        // 单段文件名：既触发不了「同名单」也触发不了保留名，只测长度这一条规则。
        return new string('a', nameLength - ".cs".Length) + ".cs";
    }

    private static GitHarvest.Core.Git.ChangedFile File(
        string path,
        string? oldPath = null,
        GitHarvest.Core.Git.ChangeKind kind = GitHarvest.Core.Git.ChangeKind.Modified)
        => new(
            path,
            oldPath,
            kind,
            GitHarvest.Core.Git.OtherChangeReason.None,
            Additions: 1,
            Deletions: 1,
            SimilarityPercent: null,
            SizeBytes: 10,
            OldBlobHash: "b0",
            NewBlobHash: "b1");
}
