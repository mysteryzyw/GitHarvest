using System.Text;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.Templates;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 导出编排（<c>IExportService.ExportAsync</c>）的单元测试：mock <see cref="IGitService"/>，
/// 输出写到真实临时目录，断言的是**产物本身**——更新包的目录树、文件名与文件内容，
/// 以及四种结论的门控（完成 / 空范围拒绝 / 冲突中止 / 失败）与取消清理。
/// 真实 git 的端到端产物见 ExportSnapshotTests（集成）。
/// </summary>
public sealed class ExportOrchestrationTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 四类变更按规则落位且未变更文件不入包()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("src/added.cs", ChangeKind.Added, oldBlob: null, newBlob: "b-add"),
                ChangedFile("src/deleted.cs", ChangeKind.Deleted, oldBlob: "b-del", newBlob: null),
                ChangedFile("src/modified.cs", ChangeKind.Modified, oldBlob: "b-mod-old", newBlob: "b-mod-new"),
                ChangedFile("src/renamed.cs", ChangeKind.Renamed, oldBlob: "b-ren-old", newBlob: "b-ren-new", oldPath: "src/old-name.cs"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b-add"] = Bytes("新增内容"),
                ["b-del"] = Bytes("删除前的旧内容"),
                ["b-mod-old"] = Bytes("修改前"),
                ["b-mod-new"] = Bytes("修改后"),
                ["b-ren-old"] = Bytes("重命名前的旧内容"),
                ["b-ren-new"] = Bytes("重命名后的新内容"),
            },
        };

        var result = await Export(git);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var package = result.PackagePath!;

        // 结构：更新前 / 更新后 / 更新说明.md
        Assert.True(Directory.Exists(UpdatePackageLayout.FolderPath(package, SnapshotSide.Before)));
        Assert.True(Directory.Exists(UpdatePackageLayout.FolderPath(package, SnapshotSide.After)));
        Assert.True(File.Exists(UpdatePackageLayout.NotesPath(package)));

        // 更新前：删除（同路径）+ 修改（同路径）+ 重命名（旧路径），新增不在其中。
        Assert.Equal(
            ["src/deleted.cs", "src/modified.cs", "src/old-name.cs"],
            RelativeFiles(package, SnapshotSide.Before));

        // 更新后：新增 + 修改 + 重命名（新路径），删除不在其中。
        Assert.Equal(
            ["src/added.cs", "src/modified.cs", "src/renamed.cs"],
            RelativeFiles(package, SnapshotSide.After));

        // 内容落在正确的路径上：重命名的旧内容在旧路径下、新内容在新路径下。
        Assert.Equal("删除前的旧内容", ReadText(package, SnapshotSide.Before, "src/deleted.cs"));
        Assert.Equal("重命名前的旧内容", ReadText(package, SnapshotSide.Before, "src/old-name.cs"));
        Assert.Equal("重命名后的新内容", ReadText(package, SnapshotSide.After, "src/renamed.cs"));

        // 只取了该取的 blob（每个待写条目一次，没有多余读取）。
        Assert.Equal(
            ["b-add", "b-del", "b-mod-new", "b-mod-old", "b-ren-new", "b-ren-old"],
            git.RequestedBlobHashes.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task 子模块指针不写快照但仍在说明清单里()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("src/a.cs", ChangeKind.Added, null, "b1"),
                ChangedFile("vendor/lib", ChangeKind.Other, "b0", "b1", reason: OtherChangeReason.SubmodulePointer),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["b1"] = Bytes("内容") },
        };

        var result = await Export(git);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.False(File.Exists(UpdatePackageLayout.FullPath(result.PackagePath!, SnapshotSide.After, "vendor/lib")));
        Assert.Equal(["b1"], git.RequestedBlobHashes);

        var notes = await File.ReadAllTextAsync(UpdatePackageLayout.NotesPath(result.PackagePath!));
        Assert.Contains("vendor/lib", notes, StringComparison.Ordinal);
        Assert.Contains("子模块指针", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 二进制内容逐字节写入()
    {
        byte[] binary = [0x00, 0x01, 0xFF, 0xFE, 0x0D, 0x0A, 0x88];
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files = [ChangedFile("logo.bin", ChangeKind.Added, null, "b-bin")],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["b-bin"] = binary },
        };

        var result = await Export(git);

        var written = await File.ReadAllBytesAsync(
            UpdatePackageLayout.FullPath(result.PackagePath!, SnapshotSide.After, "logo.bin"));
        Assert.Equal(binary, written);
    }

    [Fact]
    public async Task 更新说明以UTF8无BOM写出()
    {
        var result = await Export(GitWithOneAddedFile());

        var bytes = await File.ReadAllBytesAsync(UpdatePackageLayout.NotesPath(result.PackagePath!));

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "不应带 UTF-8 BOM");
        Assert.Contains("# 更新说明", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 空变更范围拒绝导出且不创建任何目录()
    {
        // 空范围（含基准与 Head 为同一提交）按 spec 用户故事 30：提示并禁止导出。
        var outputRoot = Path.Combine(_directory.Path, "输出");
        var git = new FakeGitService { IsAncestor = true, Files = [] };

        var result = await Export(git, outputRoot);

        Assert.False(result.IsSuccess);
        Assert.Equal(ExportOutcome.EmptyRange, result.Outcome);
        Assert.NotNull(result.FailureMessage);
        Assert.False(Directory.Exists(outputRoot));
    }

    [Fact]
    public async Task 文件系统冲突中止导出并给出完整清单()
    {
        var outputRoot = Path.Combine(_directory.Path, "输出");
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("src/a:b.cs", ChangeKind.Added, null, "b1"),
                ChangedFile("CON.txt", ChangeKind.Added, null, "b2"),
                ChangedFile("x/Order.cs", ChangeKind.Added, null, "b3"),
                ChangedFile("x/order.cs", ChangeKind.Added, null, "b4"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b1"] = Bytes("1"),
                ["b2"] = Bytes("2"),
                ["b3"] = Bytes("3"),
                ["b4"] = Bytes("4"),
            },
        };

        var result = await Export(git, outputRoot);

        Assert.Equal(ExportOutcome.ConflictBlocked, result.Outcome);
        Assert.NotNull(result.Conflicts);
        Assert.Equal(3, result.Conflicts!.Count); // 非法字符 / 保留名 / 大小写同名，一次列全
        Assert.False(Directory.Exists(outputRoot));
        Assert.Empty(git.RequestedBlobHashes); // 冲突就没开始写
    }

    [Fact]
    public async Task 目录名已存在时自动让位并告知实际路径()
    {
        var outputRoot = Path.Combine(_directory.Path, "输出");
        var taken = Path.Combine(outputRoot, "2026-09-20");
        Directory.CreateDirectory(taken);

        var result = await Export(GitWithOneAddedFile(), outputRoot);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal("2026-09-20_134500", Path.GetFileName(result.PackagePath!));
        Assert.True(Directory.Exists(taken)); // 原有目录不动
    }

    [Fact]
    public async Task 目录名非法时拒绝导出()
    {
        var outputRoot = Path.Combine(_directory.Path, "输出");

        var result = await Export(GitWithOneAddedFile(), outputRoot, folderName: @"2026\09\20");

        Assert.Equal(ExportOutcome.Failed, result.Outcome);
        Assert.Equal(ExportFailure.InvalidRequest, result.Failure);
        Assert.False(Directory.Exists(outputRoot));
    }

    [Fact]
    public async Task 输出路径为空时拒绝导出()
    {
        var result = await Export(GitWithOneAddedFile(), outputRoot: string.Empty);

        Assert.Equal(ExportOutcome.Failed, result.Outcome);
        Assert.Equal(ExportFailure.InvalidRequest, result.Failure);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public async Task 输出路径不存在时自动创建()
    {
        var outputRoot = Path.Combine(_directory.Path, "新建的", "输出路径");

        var result = await Export(GitWithOneAddedFile(), outputRoot);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.True(Directory.Exists(result.PackagePath!));
    }

    [Fact]
    public async Task 取文件内容失败时整体失败且不留下半成品()
    {
        var outputRoot = Path.Combine(_directory.Path, "输出");
        Directory.CreateDirectory(outputRoot);
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files = [ChangedFile("a.cs", ChangeKind.Added, null, "b-missing")],
            // Blobs 里没有 b-missing → 桩按「对象不存在」失败。
        };

        var result = await Export(git, outputRoot);

        Assert.Equal(ExportOutcome.Failed, result.Outcome);
        Assert.Equal(ExportFailure.GitAccess, result.Failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot)); // 半成品目录已被清理
    }

    [Fact]
    public async Task 写出过程中取消会抛出并清理半成品()
    {
        var outputRoot = Path.Combine(_directory.Path, "输出");
        Directory.CreateDirectory(outputRoot);
        using var cancellation = new CancellationTokenSource();
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("a.cs", ChangeKind.Added, null, "b1"),
                ChangedFile("b.cs", ChangeKind.Added, null, "b2"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b1"] = Bytes("第一个文件"),
                ["b2"] = Bytes("第二个文件"),
            },
        };

        // 第一个文件写完后（写出快照阶段的第一次进度）触发取消。
        var progress = new ProgressReporter(report =>
        {
            if (report.Phase == ExportPhase.WritingSnapshots && report.CompletedSteps >= 1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(git).ExportAsync(
            Request(outputRoot),
            progress,
            cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot)); // 取消后半成品已被清理
    }

    [Fact]
    public async Task 进度按阶段推进到满()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("a.cs", ChangeKind.Modified, "b-old", "b-new"),
                ChangedFile("b.cs", ChangeKind.Added, null, "b2"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b-old"] = Bytes("旧"),
                ["b-new"] = Bytes("新"),
                ["b2"] = Bytes("新增"),
            },
        };
        var progress = new ProgressReporter();

        var result = await CreateService(git).ExportAsync(Request(Path.Combine(_directory.Path, "输出")), progress);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Contains(progress.Reports, report => report.Phase == ExportPhase.ComputingRange);
        Assert.Contains(progress.Reports, report => report.Phase == ExportPhase.ScanningConflicts);
        Assert.Contains(progress.Reports, report => report.Phase == ExportPhase.WritingSnapshots);
        Assert.Contains(progress.Reports, report => report.Phase == ExportPhase.WritingNotes);
        Assert.Equal(100, progress.Reports[^1].Percent);
        Assert.Equal(result.BeforeFileCount + result.AfterFileCount + 1, progress.Reports[^1].TotalSteps);
    }

    [Fact]
    public async Task 结果给出两侧文件数与总字节()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("a.cs", ChangeKind.Modified, "b-old", "b-new"),
                ChangedFile("d.cs", ChangeKind.Deleted, "b-del", null),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b-old"] = Bytes("12345"),
                ["b-new"] = Bytes("123"),
                ["b-del"] = Bytes("12"),
            },
        };

        var result = await Export(git);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(2, result.BeforeFileCount); // 修改旧版 + 删除
        Assert.Equal(1, result.AfterFileCount);  // 修改新版
        Assert.Equal(0, result.AddedFileCount);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.True(result.TotalBytes > 10, $"总字节数应包含快照与说明，实际 {result.TotalBytes}");
    }

    [Fact]
    public async Task 只有新增时更新前文件夹仍存在且为空()
    {
        var result = await Export(GitWithOneAddedFile());

        var beforeFolder = UpdatePackageLayout.FolderPath(result.PackagePath!, SnapshotSide.Before);
        Assert.True(Directory.Exists(beforeFolder), "更新包结构固定含两个文件夹");
        Assert.Empty(Directory.EnumerateFiles(beforeFolder, "*", SearchOption.AllDirectories));
        Assert.Equal(0, result.BeforeFileCount);
    }

    [Fact]
    public async Task 预检给出结构与实际目录名且不写出任何文件()
    {
        var taken = Path.Combine(_directory.Path, "输出", "2026-09-20");
        Directory.CreateDirectory(taken);
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("a.cs", ChangeKind.Modified, "b-old", "b-new"),
                ChangedFile("b.cs", ChangeKind.Added, null, "b2"),
                ChangedFile("d.cs", ChangeKind.Deleted, "b-del", null),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b-old"] = Bytes("旧"),
                ["b-new"] = Bytes("新"),
                ["b2"] = Bytes("新增"),
                ["b-del"] = Bytes("删除"),
            },
        };

        var result = await CreateService(git).InspectAsync(Request(Path.Combine(_directory.Path, "输出")));

        Assert.True(result.IsReady);
        Assert.Null(result.Conflicts);
        Assert.NotNull(result.Plan);
        Assert.Equal("2026-09-20_134500", result.Plan!.FolderName); // 已存在 → 让位
        Assert.Equal(2, result.Plan.BeforeFileCount);               // 修改旧版 + 删除
        Assert.Equal(2, result.Plan.AfterFileCount);                // 修改新版 + 新增
        Assert.Equal(1, result.Plan.AddedFileCount);
        Assert.Equal(1, result.Plan.DeletedFileCount);

        // 预检只算不写：目录没被创建，也没去取任何文件内容。
        Assert.False(Directory.Exists(result.Plan.PackagePath));
        Assert.Empty(git.RequestedBlobHashes);
    }

    [Fact]
    public async Task 预检发现冲突时仍给出结构与完整清单()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("src/a:b.cs", ChangeKind.Added, null, "b1"),
                ChangedFile("CON.txt", ChangeKind.Added, null, "b2"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["b1"] = Bytes("1"), ["b2"] = Bytes("2") },
        };

        var result = await CreateService(git).InspectAsync(Request(Path.Combine(_directory.Path, "输出")));

        Assert.True(result.IsBlockedByConflicts);
        Assert.False(result.IsReady);
        Assert.NotNull(result.Plan);                 // 结构照常给出（界面可展示「本可包含什么」）
        Assert.Equal(2, result.Conflicts!.Count);
        Assert.NotNull(result.FailureMessage);
        Assert.Empty(git.RequestedBlobHashes);
    }

    [Fact]
    public async Task 预检报告空范围与非法请求()
    {
        var empty = await CreateService(new FakeGitService { IsAncestor = true, Files = [] })
            .InspectAsync(Request(Path.Combine(_directory.Path, "输出")));

        Assert.True(empty.IsEmptyRange);
        Assert.Null(empty.Plan);

        var noOutput = await CreateService(GitWithOneAddedFile())
            .InspectAsync(Request(outputRoot: string.Empty));

        Assert.True(noOutput.IsFailed);
        Assert.Equal(ExportFailure.InvalidRequest, noOutput.Failure);
    }

    [Fact]
    public async Task 编辑后的说明内容随更新包写出且占位符照常渲染()
    {
        // 用户故事 40：编辑器里的本次内容替代模板生成结果（插入的占位符仍按当前范围解析）。
        var request = Request(Path.Combine(_directory.Path, "输出")) with
        {
            NotesOverride = "# 定制标题\r\n分支：{分支名}\r\n范围 {基准哈希}..{Head哈希}\r\n（手写内容）",
        };

        var result = await CreateService(GitWithOneAddedFile()).ExportAsync(request);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var notes = await File.ReadAllTextAsync(UpdatePackageLayout.NotesPath(result.PackagePath!));
        Assert.Contains("# 定制标题", notes, StringComparison.Ordinal);
        Assert.Contains("分支：main", notes, StringComparison.Ordinal);
        Assert.Contains("范围 a1b2c3d..e4f5g6h", notes, StringComparison.Ordinal);
        // 编辑器给的是 Windows 换行：写出统一为 LF（跨工具兼容，与 UTF-8 无 BOM 同一约定）。
        Assert.DoesNotContain("\r\n", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 编辑内容里的未知占位符原样保留且导出照常完成()
    {
        var request = Request(Path.Combine(_directory.Path, "输出")) with
        {
            NotesOverride = "笔误占位符：{更新曰期}",
        };

        var result = await CreateService(GitWithOneAddedFile()).ExportAsync(request);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var notes = await File.ReadAllTextAsync(UpdatePackageLayout.NotesPath(result.PackagePath!));
        Assert.Contains("{更新曰期}", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 自定义模板生效而读取失败时回退内置()
    {
        var templatePath = Path.Combine(_directory.Path, "自定义模板.md");
        await File.WriteAllTextAsync(templatePath, "# 自定义说明\n{变更统计}");
        var settings = new StubSettingsService
        {
            Settings = new GitHarvest.Core.Settings.GlobalSettings { DefaultTemplatePath = templatePath },
        };

        var customResult = await CreateService(GitWithOneAddedFile(), CreateTemplateService(settings))
            .ExportAsync(Request(Path.Combine(_directory.Path, "输出一")));
        var customNotes = await File.ReadAllTextAsync(UpdatePackageLayout.NotesPath(customResult.PackagePath!));
        Assert.Contains("# 自定义说明", customNotes, StringComparison.Ordinal);
        Assert.Contains("共 1 个文件", customNotes, StringComparison.Ordinal);

        // 模板文件被删：回退内置模板照常导出（用户故事 38：自定义不会导致导出失败）。
        File.Delete(templatePath);
        var fallbackResult = await CreateService(GitWithOneAddedFile(), CreateTemplateService(settings))
            .ExportAsync(Request(Path.Combine(_directory.Path, "输出二")));
        var fallbackNotes = await File.ReadAllTextAsync(UpdatePackageLayout.NotesPath(fallbackResult.PackagePath!));
        Assert.Contains("# 更新说明", fallbackNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 预检带出变更范围汇总供说明草稿使用()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                ChangedFile("a.cs", ChangeKind.Added, null, "b1"),
                ChangedFile("b.cs", ChangeKind.Modified, "b-old", "b-new"),
            ],
            Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["b1"] = Bytes("1"),
                ["b-old"] = Bytes("旧"),
                ["b-new"] = Bytes("新"),
            },
        };

        var result = await CreateService(git).InspectAsync(Request(Path.Combine(_directory.Path, "输出")));

        Assert.True(result.IsReady);
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary!.TotalCount);
        Assert.Equal(1, result.Summary.CountOf(ChangeKind.Added));
    }

    public void Dispose() => _directory.Dispose();

    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private static ExportService CreateService(FakeGitService git, ITemplateService? templateService = null)
        => new(git, templateService ?? CreateTemplateService(), SilentLogger);

    /// <summary>默认只给内置模板（不碰磁盘的桩设置）；自定义模板场景由调用方显式构造。</summary>
    private static TemplateService CreateTemplateService(StubSettingsService? settings = null)
        => new(settings ?? new StubSettingsService(), SilentLogger);

    private static FakeGitService GitWithOneAddedFile() => new()
    {
        IsAncestor = true,
        Files = [ChangedFile("a.cs", ChangeKind.Added, null, "b1")],
        Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["b1"] = Bytes("内容") },
    };

    private Task<ExportResult> Export(
        FakeGitService git,
        string? outputRoot = null,
        string folderName = "2026-09-20")
        => CreateService(git).ExportAsync(
            Request(outputRoot ?? Path.Combine(_directory.Path, "输出"), folderName));

    private static ExportRequest Request(string outputRoot, string folderName = "2026-09-20")
        => new(
            RepositoryPath: @"D:\Code\DemoService",
            OutputRootPath: outputRoot,
            FolderName: folderName,
            RequestedAt: new DateTimeOffset(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8)),
            BranchName: "main",
            Base: new CommitSummary("a1b2c3d", "chore(release): 2.4.0 版本冻结", "王磊", DateTimeOffset.Now),
            Head: new CommitSummary("e4f5g6h", "feat(export): 支持更新说明模板占位符", "张伟", DateTimeOffset.Now));

    private static ChangedFile ChangedFile(
        string path,
        ChangeKind kind,
        string? oldBlob,
        string? newBlob,
        string? oldPath = null,
        OtherChangeReason reason = OtherChangeReason.None)
        => new(path, oldPath, kind, reason, Additions: 1, Deletions: 0, SimilarityPercent: null, SizeBytes: 10, oldBlob, newBlob);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>列出某一侧里写出的全部文件（相对该侧文件夹，正斜杠风格，排序后便于断言）。</summary>
    private static string[] RelativeFiles(string packagePath, SnapshotSide side)
    {
        var folder = UpdatePackageLayout.FolderPath(packagePath, side);
        return
        [
            .. Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string ReadText(string packagePath, SnapshotSide side, string relativePath)
        => File.ReadAllText(UpdatePackageLayout.FullPath(packagePath, side, relativePath), Encoding.UTF8);

    /// <summary>同步收集进度报告的桩（测试里不需要真异步投递，也不需要界面上下文）。</summary>
    private sealed class ProgressReporter : IProgress<ExportProgress>
    {
        private readonly Action<ExportProgress>? _onReport;

        public ProgressReporter(Action<ExportProgress>? onReport = null) => _onReport = onReport;

        public List<ExportProgress> Reports { get; } = [];

        public void Report(ExportProgress value)
        {
            Reports.Add(value);
            _onReport?.Invoke(value);
        }
    }
}
