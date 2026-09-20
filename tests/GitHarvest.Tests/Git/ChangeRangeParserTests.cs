using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Git;

/// <summary>
/// git 差异输出解析与变更类型归类的单元测试（纯函数，不跑 git）：
/// raw / numstat / ls-tree 三份 -z 输出的解析容错，以及状态字母 → 变更类型的映射
/// （含 gitlink 归「其他」、重命名相似度、无法识别字母不丢条目）。
/// 端到端（真实 git 产出这些输出）见 GetChangeRangeTests。
/// </summary>
public sealed class ChangeRangeParserTests
{
    // ============ ParseRaw：git diff --raw -M -z --no-abbrev ============

    [Fact]
    public void ParseRaw解析增删改三类记录()
    {
        var output = string.Join('\0',
            ":000000 100644 0000000000000000000000000000000000000000 aaa A",
            "new.txt",
            ":100644 000000 bbb 0000000000000000000000000000000000000000 D",
            "old.txt",
            ":100644 100644 ccc ddd M",
            "mod.txt",
            "") + '\0';

        var records = ChangeRangeParser.ParseRaw(output);

        Assert.NotNull(records);
        Assert.Equal(3, records!.Count);

        var added = Assert.Single(records, r => r.StatusLetter == "A");
        Assert.Equal("new.txt", added.Path);
        Assert.Null(added.NewPath);
        Assert.Null(added.OldBlobHash); // 全零占位 → 无该侧版本
        Assert.Equal("aaa", added.NewBlobHash);

        var deleted = Assert.Single(records, r => r.StatusLetter == "D");
        Assert.Equal("old.txt", deleted.Path);
        Assert.Equal("bbb", deleted.OldBlobHash);
        Assert.Null(deleted.NewBlobHash);

        var modified = Assert.Single(records, r => r.StatusLetter == "M");
        Assert.Equal("ccc", modified.OldBlobHash);
        Assert.Equal("ddd", modified.NewBlobHash);
    }

    [Fact]
    public void ParseRaw解析重命名记录的旧新路径与相似度()
    {
        var output = ":100644 100644 aaa bbb R87\0src/old.rs\0src/new.rs\0";

        var records = ChangeRangeParser.ParseRaw(output);

        Assert.NotNull(records);
        var record = Assert.Single(records);
        Assert.Equal("R", record.StatusLetter);
        Assert.Equal(87, record.Score);
        Assert.Equal("src/old.rs", record.Path);
        Assert.Equal("src/new.rs", record.NewPath);
        Assert.Equal("src/new.rs", record.CurrentPath);
    }

    [Fact]
    public void ParseRaw普通记录的当前路径取原路径()
    {
        var records = ChangeRangeParser.ParseRaw(":100644 100644 aaa bbb M\0dir/mod.txt\0");

        Assert.NotNull(records);
        Assert.Equal("dir/mod.txt", Assert.Single(records).CurrentPath);
    }

    [Fact]
    public void ParseRaw字段数不足的记录判为整体失败()
    {
        // 记录头后没有路径字段——输出形态异常，宁可让调用方按 Git 错误处理也不静默丢条目。
        Assert.Null(ChangeRangeParser.ParseRaw(":100644 100644 aaa bbb M\0"));
    }

    [Fact]
    public void ParseRaw状态带未知分数后缀时视为无法解析()
    {
        // 分数必须是纯数字（R100）；出现其他形态说明输出与预期格式不符。
        Assert.Null(ChangeRangeParser.ParseRaw(":100644 100644 aaa bbb RX9\0a.txt\0"));
    }

    [Fact]
    public void ParseRaw空输出得到空清单()
    {
        var records = ChangeRangeParser.ParseRaw(string.Empty);

        Assert.NotNull(records);
        Assert.Empty(records!);
    }

    [Fact]
    public void ParseRaw子模块记录标记为gitlink()
    {
        var records = ChangeRangeParser.ParseRaw(":160000 160000 aaa bbb M\0vendor/lib\0");

        Assert.NotNull(records);
        var record = Assert.Single(records);
        Assert.True(record.InvolvesSubmodule);
        Assert.Equal(ChangeRangeParser.GitlinkMode, record.OldMode);
    }

    // ============ ParseNumstat：git diff --numstat -M -z ============

    [Fact]
    public void ParseNumstat解析增删行数与二进制占位()
    {
        // 实测格式：数字与路径之间是制表符（普通记录），二进制两侧均为 "-"。
        var output = string.Join('\0',
            "12\t3\ta.txt",
            "-\t-\tassets/logo.png",
            "") + '\0';

        var records = ChangeRangeParser.ParseNumstat(output);

        Assert.NotNull(records);
        Assert.Equal(2, records!.Count);
        Assert.Equal((12, 3), (records[0].Additions, records[0].Deletions));
        Assert.Equal("a.txt", records[0].CurrentPath);
        var binary = records[1];
        Assert.Null(binary.Additions);
        Assert.Null(binary.Deletions);
        Assert.Equal("assets/logo.png", binary.CurrentPath);
    }

    [Fact]
    public void ParseNumstat解析重命名记录的旧新路径()
    {
        // 重命名记录：数字后的路径位为空，旧新路径是随后的两个 NUL 字段。
        var records = ChangeRangeParser.ParseNumstat("0\t5\t\0src/old.rs\0src/new.rs\0");

        Assert.NotNull(records);
        var record = Assert.Single(records);
        Assert.Equal("src/old.rs", record.Path);
        Assert.Equal("src/new.rs", record.NewPath);
        Assert.Equal("src/new.rs", record.CurrentPath);
    }

    [Fact]
    public void ParseNumstat数字字段非法时判为整体失败()
    {
        Assert.Null(ChangeRangeParser.ParseNumstat("多\t3\ta.txt\0"));
    }

    [Fact]
    public void ParseNumstat混合流中普通记录与重命名记录边界正确()
    {
        // 一条普通记录后紧跟一条重命名记录：重命名记录以「增\t删\t\0」的空路径位开启。
        var output = string.Join('\0',
            "1\t2\tnormal.txt",
            "0\t5\t",
            "src/old.rs",
            "src/new.rs",
            "") + '\0';

        var records = ChangeRangeParser.ParseNumstat(output);

        Assert.NotNull(records);
        Assert.Equal(2, records!.Count);
        Assert.Null(records[0].NewPath);
        Assert.Equal("normal.txt", records[0].CurrentPath);
        Assert.Equal("src/new.rs", records[1].CurrentPath);
    }

    // ============ ParseTreeSizes：git ls-tree -r -l ============

    [Fact]
    public void ParseTreeSizes解析路径到字节数()
    {
        var output = string.Join('\n',
            "100644 blob aaa      10\tadd.txt",
            "100644 blob bbb     921\tkeep.txt",
            "");

        var sizes = ChangeRangeParser.ParseTreeSizes(output);

        Assert.NotNull(sizes);
        Assert.Equal(10L, sizes!["add.txt"]);
        Assert.Equal(921L, sizes["keep.txt"]);
    }

    [Fact]
    public void ParseTreeSizes把gitlink的减号占位解析为无大小()
    {
        var output = "160000 commit ccc       -\tvendor/lib\n";

        var sizes = ChangeRangeParser.ParseTreeSizes(output);

        Assert.NotNull(sizes);
        Assert.True(sizes.ContainsKey("vendor/lib"));
        Assert.Null(sizes!["vendor/lib"]);
    }

    [Fact]
    public void ParseTreeSizes缺大小字段的行判为整体失败()
    {
        Assert.Null(ChangeRangeParser.ParseTreeSizes("100644 blob aaa\tadd.txt\n"));
    }

    // ============ BuildChangedFiles：合成与展示大小取值 ============

    [Fact]
    public void BuildChangedFiles按状态字母归类五类变更()
    {
        var records = ChangeRangeParser.ParseRaw(string.Join('\0',
            ":000000 100644 0 a A", "added.txt",
            ":100644 000000 b 0 D", "deleted.txt",
            ":100644 100644 c d M", "modified.txt",
            ":100644 100644 e f R90", "old.txt", "new.txt",
            ":100644 120000 g h T", "link.txt",
            ":160000 160000 i j M", "vendor/lib",
            "") + '\0')!;

        var files = ChangeRangeParser.BuildChangedFiles(records, [], EmptySizes, EmptySizes);

        Assert.Equal(ChangeKind.Added, File("added.txt").Kind);
        Assert.Equal(ChangeKind.Deleted, File("deleted.txt").Kind);
        Assert.Equal(ChangeKind.Modified, File("modified.txt").Kind);
        var renamed = File("new.txt");
        Assert.Equal(ChangeKind.Renamed, renamed.Kind);
        Assert.Equal("old.txt", renamed.OldPath);
        Assert.Equal(90, renamed.SimilarityPercent);
        Assert.Equal(ChangeKind.Other, File("link.txt").Kind);
        Assert.Equal(OtherChangeReason.TypeChange, File("link.txt").OtherReason);
        Assert.Equal(ChangeKind.Other, File("vendor/lib").Kind);
        Assert.Equal(OtherChangeReason.SubmodulePointer, File("vendor/lib").OtherReason);

        ChangedFile File(string path) => Assert.Single(files, f => f.Path == path);
    }

    [Fact]
    public void BuildChangedFiles子模块的新增删除与指针变化一律归其他()
    {
        // 子模块（gitlink）的任何一种变化都不算「文件新增 / 删除 / 修改」：记录的是指针，
        // 没有可导出的内容（实测 git 报 A / D / M 三种形，换路径只会报 D+A、不报 R）。
        var records = ChangeRangeParser.ParseRaw(string.Join('\0',
            ":000000 160000 0 a A", "vendor/new-lib",
            ":160000 000000 b 0 D", "vendor/gone-lib",
            ":160000 160000 c d M", "vendor/lib",
            ":000000 100644 0 e A", "added.txt",
            ":100644 000000 f 0 D", "deleted.txt",
            "") + '\0')!;

        var files = ChangeRangeParser.BuildChangedFiles(records, [], EmptySizes, EmptySizes);

        foreach (var path in new[] { "vendor/new-lib", "vendor/gone-lib", "vendor/lib" })
        {
            var file = Find(path);
            Assert.Equal(ChangeKind.Other, file.Kind);
            Assert.Equal(OtherChangeReason.SubmodulePointer, file.OtherReason);
        }

        // gitlink 判定不能外溢：非 160000 的条目仍按 A / D 归类。
        Assert.Equal(ChangeKind.Added, Find("added.txt").Kind);
        Assert.Equal(ChangeKind.Deleted, Find("deleted.txt").Kind);

        ChangedFile Find(string path) => Assert.Single(files, f => f.Path == path);
    }

    [Fact]
    public void BuildChangedFiles复制与未合并与未知字母归其他()
    {
        var records = ChangeRangeParser.ParseRaw(string.Join('\0',
            ":100644 100644 a b C80", "src/a.cs", "copy.cs",
            ":100644 100644 c d U", "conflict.txt",
            ":100644 100644 e f X", "odd.txt",
            "") + '\0')!;

        var files = ChangeRangeParser.BuildChangedFiles(records, [], EmptySizes, EmptySizes);

        Assert.Equal(OtherChangeReason.Copied, files[0].OtherReason);
        Assert.Equal(80, files[0].SimilarityPercent);
        Assert.Equal(OtherChangeReason.Unmerged, files[1].OtherReason);
        Assert.Equal(OtherChangeReason.Unknown, files[2].OtherReason);
    }

    [Fact]
    public void BuildChangedFiles关联行数与二进制占位()
    {
        var records = ChangeRangeParser.ParseRaw(string.Join('\0',
            ":100644 100644 a b M", "text.txt",
            ":100644 100644 c d M", "blob.png",
            "") + '\0')!;
        var numstats = ChangeRangeParser.ParseNumstat(string.Join('\0',
            "12\t3\ttext.txt",
            "-\t-\tblob.png",
            "") + '\0')!;

        var files = ChangeRangeParser.BuildChangedFiles(records, numstats, EmptySizes, EmptySizes);

        Assert.Equal((12, 3), (files[0].Additions, files[0].Deletions));
        Assert.True(files[0].HasLineCounts);
        Assert.Null(files[1].Additions);
        Assert.Null(files[1].Deletions);
        Assert.False(files[1].HasLineCounts);
    }

    [Fact]
    public void BuildChangedFiles新增取更新后大小而删除取更新前大小()
    {
        var records = ChangeRangeParser.ParseRaw(string.Join('\0',
            ":000000 100644 0 a A", "added.txt",
            ":100644 000000 b 0 D", "deleted.txt",
            ":100644 100644 c d M", "modified.txt",
            ":100644 100644 e f R100", "old.txt", "new.txt",
            ":160000 160000 g h M", "vendor/lib",
            "") + '\0')!;
        var sizesAtBase = new Dictionary<string, long?> { ["deleted.txt"] = 11, ["old.txt"] = 22 };
        var sizesAtHead = new Dictionary<string, long?> { ["added.txt"] = 33, ["modified.txt"] = 44, ["new.txt"] = 55, ["vendor/lib"] = null };

        var files = ChangeRangeParser.BuildChangedFiles(records, [], sizesAtBase, sizesAtHead);

        Assert.Equal(33L, File("added.txt").SizeBytes);
        Assert.Equal(11L, File("deleted.txt").SizeBytes);
        Assert.Equal(44L, File("modified.txt").SizeBytes);
        Assert.Equal(55L, File("new.txt").SizeBytes); // 重命名取更新后（新路径）的大小
        Assert.Null(File("vendor/lib").SizeBytes);    // 子模块指针没有内容大小

        ChangedFile File(string path) => Assert.Single(files, f => f.Path == path);
    }

    [Fact]
    public void BuildChangedFiles大小表缺失的路径保持无大小()
    {
        var records = ChangeRangeParser.ParseRaw(":000000 100644 0 a A\0ghost.txt\0")!;

        var files = ChangeRangeParser.BuildChangedFiles(records, [], EmptySizes, EmptySizes);

        var file = Assert.Single(files);
        Assert.Null(file.SizeBytes);
    }

    private static readonly Dictionary<string, long?> EmptySizes = [];
}
