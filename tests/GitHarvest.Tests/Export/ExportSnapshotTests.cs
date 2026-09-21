using System.Text;
using System.Text.Json;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.History;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 对真实仓库导出更新包的端到端测试（真实 git.exe + 真实文件系统）：
/// 这是 spec 用户故事 24～35 的总验收——产物目录树、文件内容、更新说明编码、
/// 目录命名让位、空范围拒绝、bare 仓库可用，以及导出前的文件系统冲突中止。
/// 冲突里只有「仅大小写不同」与「超长路径」能用真实仓库造出来（Windows 上 git 直接拒绝
/// 含非法字符与保留设备名的路径，那两类由 FileSystemConflictScannerTests 的纯函数测试覆盖）。
/// </summary>
public sealed class ExportSnapshotTests : IDisposable
{
    /// <summary>导出时「此刻」的时间点，让 _HHmmss 后缀可断言。</summary>
    private static readonly DateTimeOffset Moment =
        new(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8));

    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 真实仓库的导出产物符合快照规则()
    {
        var repositoryPath = await CreateRepositoryAsync("export-e2e");
        await CommitFileAsync(repositoryPath, "keep.txt", "未变更的文件", "c1");
        await CommitFileAsync(repositoryPath, "deleted.txt", "删除前的旧内容", "c1");
        await CommitFileAsync(repositoryPath, "modified.txt", "修改前", "c1");
        await CommitFileAsync(repositoryPath, "old-name.txt", TenLines("原内容"), "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");

        // c2（Head）：新增（含中文路径）、删除、修改、重命名、二进制。
        await CommitFileAsync(repositoryPath, Path.Combine("模块", "新增.json"), "{\"n\":1}", "c2");
        await TestGit.RunAsync(["rm", "-q", "deleted.txt"], repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "modified.txt"), "修改后");
        await TestGit.RunAsync(["mv", "old-name.txt", "新名.txt"], repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "新名.txt"), TenLines("改了一行"));
        await File.WriteAllBytesAsync(Path.Combine(repositoryPath, "logo.bin"), [0x00, 0x01, 0xFF, 0xFE, 0x0A]);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "c2"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var outputRoot = Path.Combine(_directory.Path, "交付物");
        var result = await ExportAsync(repositoryPath, baseHash, headHash, outputRoot);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var package = result.PackagePath!;
        Assert.Equal("2026-09-20", Path.GetFileName(package));

        // 更新前：删除 + 修改旧版 + 重命名的旧路径。
        Assert.Equal(
            ["deleted.txt", "modified.txt", "old-name.txt"],
            RelativeFiles(package, SnapshotSide.Before));

        // 更新后：新增（中文路径）+ 修改新版 + 重命名的新路径 + 二进制。
        Assert.Equal(
            ["logo.bin", "modified.txt", "新名.txt", "模块/新增.json"],
            RelativeFiles(package, SnapshotSide.After));

        // 内容与提交里的字节一致。
        Assert.Equal("删除前的旧内容", ReadText(package, SnapshotSide.Before, "deleted.txt"));
        Assert.Equal("修改前", ReadText(package, SnapshotSide.Before, "modified.txt"));
        Assert.Equal(TenLines("原内容"), ReadText(package, SnapshotSide.Before, "old-name.txt"));
        Assert.Equal("修改后", ReadText(package, SnapshotSide.After, "modified.txt"));
        Assert.Equal(TenLines("改了一行"), ReadText(package, SnapshotSide.After, "新名.txt"));
        Assert.Equal("{\"n\":1}", ReadText(package, SnapshotSide.After, "模块/新增.json"));
        Assert.Equal(
            [0x00, 0x01, 0xFF, 0xFE, 0x0A],
            await File.ReadAllBytesAsync(UpdatePackageLayout.FullPath(package, SnapshotSide.After, "logo.bin")));

        // 未变更的文件不进包，仓库元数据也不进包。
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(package, "*", SearchOption.AllDirectories),
            entry => entry.Contains("keep.txt", StringComparison.Ordinal) || entry.Contains(".git", StringComparison.Ordinal));

        // 结果里的数字与目录实测一致（新增：模块/新增.json 与 logo.bin）。
        Assert.Equal(3, result.BeforeFileCount);
        Assert.Equal(4, result.AfterFileCount);
        Assert.Equal(2, result.AddedFileCount);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.True(result.TotalBytes > 0);
    }

    [Fact]
    public async Task 更新说明记录范围信息且为UTF8无BOM()
    {
        var repositoryPath = await CreateRepositoryAsync("export-notes");
        await CommitFileAsync(repositoryPath, "a.txt", "内容", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await CommitFileAsync(repositoryPath, "b.txt", "新内容", "c2：新增文件");
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await ExportAsync(repositoryPath, baseHash, headHash, Path.Combine(_directory.Path, "交付物"));

        var bytes = await File.ReadAllBytesAsync(UpdatePackageLayout.NotesPath(result.PackagePath!));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "更新说明不应带 UTF-8 BOM");

        var notes = Encoding.UTF8.GetString(bytes);
        Assert.Contains(await ShortAsync(repositoryPath, baseHash), notes, StringComparison.Ordinal);
        Assert.Contains(await ShortAsync(repositoryPath, headHash), notes, StringComparison.Ordinal);
        Assert.Contains("c2：新增文件", notes, StringComparison.Ordinal);
        Assert.Contains("b.txt", notes, StringComparison.Ordinal);
        Assert.Contains("共 1 个文件", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 基准与Head相同时拒绝导出且不创建目录()
    {
        var repositoryPath = await CreateRepositoryAsync("export-empty", commitCount: 2);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");
        var outputRoot = Path.Combine(_directory.Path, "交付物");

        var result = await ExportAsync(repositoryPath, headHash, headHash, outputRoot);

        Assert.Equal(ExportOutcome.EmptyRange, result.Outcome);
        Assert.False(Directory.Exists(outputRoot));
    }

    [Fact]
    public async Task 同名目录已存在时让位并告知实际目录()
    {
        var repositoryPath = await CreateRepositoryAsync("export-name-conflict");
        await CommitFileAsync(repositoryPath, "a.txt", "内容", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await CommitFileAsync(repositoryPath, "b.txt", "新内容", "c2");
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var outputRoot = Path.Combine(_directory.Path, "交付物");
        var taken = Path.Combine(outputRoot, "2026-09-20");
        Directory.CreateDirectory(taken);
        await File.WriteAllTextAsync(Path.Combine(taken, "同名的旧目录.txt"), "不要被覆盖");

        var result = await ExportAsync(repositoryPath, baseHash, headHash, outputRoot);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal("2026-09-20_134500", Path.GetFileName(result.PackagePath!));
        Assert.True(File.Exists(Path.Combine(taken, "同名的旧目录.txt")), "已存在的目录内容不能被改动");
    }

    [Fact]
    public async Task bare仓库也能导出更新包()
    {
        // spec：导出只需要提交数据，bare 仓库（无工作区）应当可用。
        // 先在工作仓库造出真实的文件变更，再克隆成 bare 形态（bare 仓库自己不能提交）。
        var workingPath = await CreateRepositoryAsync("export-bare-work");
        await CommitFileAsync(workingPath, "a.txt", "旧内容", "c1");
        await CommitFileAsync(workingPath, "a.txt", "新内容", "c2");
        var barePath = Path.Combine(_directory.Path, "export-bare.git");
        await TestGit.RunAsync(["clone", "-q", "--bare", workingPath, barePath]);

        var baseHash = await ResolveAsync(barePath, "HEAD~1");
        var headHash = await ResolveAsync(barePath, "HEAD");
        var result = await ExportAsync(barePath, baseHash, headHash, Path.Combine(_directory.Path, "交付物"));

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal("新内容", ReadText(result.PackagePath!, SnapshotSide.After, "a.txt"));
        Assert.Equal("旧内容", ReadText(result.PackagePath!, SnapshotSide.Before, "a.txt"));
    }

    [Fact]
    public async Task 存在仅大小写不同的同名路径时中止导出()
    {
        var repositoryPath = await CreateRepositoryAsync("export-case-collision");
        await CommitFileAsync(repositoryPath, "keep.txt", "内容", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        // Windows 的工作区建不出「仅大小写不同」的两个文件，但索引可以（层级里确实存在这种仓库）。
        await AddIndexOnlyFileAsync(repositoryPath, "src/Order.cs", "keep.txt");
        await AddIndexOnlyFileAsync(repositoryPath, "src/order.cs", "keep.txt");
        await TestGit.RunAsync(["commit", "-q", "-m", "大小写同名"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var outputRoot = Path.Combine(_directory.Path, "交付物");
        var result = await ExportAsync(repositoryPath, baseHash, headHash, outputRoot);

        Assert.Equal(ExportOutcome.ConflictBlocked, result.Outcome);
        Assert.Contains(result.Conflicts!, conflict => conflict.Kind == FileSystemConflictKind.CaseCollision);
        Assert.False(Directory.Exists(outputRoot), "冲突中止不应留下任何目录");
    }

    [Fact]
    public async Task 完整路径超长时中止导出()
    {
        var repositoryPath = await CreateRepositoryAsync("export-long-path");
        await CommitFileAsync(repositoryPath, "keep.txt", "内容", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        // 四层 60 字符的目录 + 文件名：加上输出路径后必然越过 Windows 的 260 字符上限。
        var deepPath = string.Join('/', Enumerable.Repeat(new string('d', 60), 4)) + "/x.cs";
        await AddIndexOnlyFileAsync(repositoryPath, deepPath, "keep.txt");
        await TestGit.RunAsync(["commit", "-q", "-m", "超长路径"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var outputRoot = Path.Combine(_directory.Path, "交付物");
        var result = await ExportAsync(repositoryPath, baseHash, headHash, outputRoot);

        Assert.Equal(ExportOutcome.ConflictBlocked, result.Outcome);
        Assert.Contains(result.Conflicts!, conflict => conflict.Kind == FileSystemConflictKind.PathTooLong);
        Assert.False(Directory.Exists(outputRoot));
    }

    [Fact]
    public async Task 真实导出会往导出历史追加一条记录()
    {
        var repositoryPath = await CreateRepositoryAsync("export-history-e2e");
        await CommitFileAsync(repositoryPath, "a.txt", "旧内容", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await CommitFileAsync(repositoryPath, "a.txt", "新内容", "c2");
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        // 这一次用真实的 HistoryService（落到临时目录的 JSONL），验证的是「真的写进了文件」。
        var historyFilePath = Path.Combine(_directory.Path, "数据", "export-history.jsonl");
        var outputRoot = Path.Combine(_directory.Path, "交付物");
        var result = await CreateService(new HistoryService(historyFilePath, SilentLogger))
            .ExportAsync(await RequestAsync(repositoryPath, baseHash, headHash, outputRoot));

        Assert.True(result.IsSuccess, result.FailureMessage);

        // 新实例模拟重启：统计与「上次导出」重启后依然在，说明记录真的落了盘。
        var reopened = new HistoryService(historyFilePath, SilentLogger);
        var statistics = reopened.GetStatistics();
        Assert.Equal(1, statistics.PackageCount);
        Assert.Equal("main", statistics.MostUsedBranch);
        Assert.Equal(Moment, reopened.GetLastExportedAt(repositoryPath));

        // 文件里就一行，且记录的是实际落地的输出路径（JSON 里反斜杠会转义，按 JSON 解析后比对）。
        using var line = JsonDocument.Parse(File.ReadLines(historyFilePath).Single());
        Assert.Equal(result.PackagePath!, line.RootElement.GetProperty("outputPath").GetString());
    }

    public void Dispose() => _directory.Dispose();

    private static ExportService CreateService(IHistoryService? historyService = null)
        => new(
            new GitService(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger),
            new GitHarvest.Core.Templates.TemplateService(new StubSettingsService(), SilentLogger),
            // 默认用桩接住导出历史：真实 git 测试不该把历史写进用户目录；
            // 「真写文件」这一条由 真实导出会往导出历史追加一条记录 显式覆盖。
            historyService ?? new RecordingHistoryService(),
            SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount = 0)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount);

    /// <summary>用与产品同一套入口导出（不绕过编排）：范围、目录名与说明信息都按真实仓库给出。</summary>
    private async Task<ExportResult> ExportAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        string outputRoot)
        => await CreateService().ExportAsync(await RequestAsync(repositoryPath, baseHash, headHash, outputRoot));

    private async Task<ExportRequest> RequestAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        string outputRoot)
        => new(
            repositoryPath,
            outputRoot,
            "2026-09-20",
            Moment,
            BranchName: "main",
            Base: new CommitSummary(
                await ShortAsync(repositoryPath, baseHash),
                await SubjectAsync(repositoryPath, baseHash),
                AuthorName: "测试",
                Moment),
            Head: new CommitSummary(
                await ShortAsync(repositoryPath, headHash),
                await SubjectAsync(repositoryPath, headHash),
                AuthorName: "测试",
                Moment));

    private async Task<string> ResolveAsync(string repositoryPath, string reference)
        => await GitOutputAsync(repositoryPath, ["rev-parse", reference]);

    private async Task<string> ShortAsync(string repositoryPath, string hash)
        => await GitOutputAsync(repositoryPath, ["rev-parse", "--short", hash]);

    private async Task<string> SubjectAsync(string repositoryPath, string hash)
        => await GitOutputAsync(repositoryPath, ["log", "-1", "--format=%s", hash]);

    private static async Task<string> GitOutputAsync(string repositoryPath, IReadOnlyList<string> arguments)
    {
        var output = await new GitCliRunner().RunAsync(TestGit.Invocation(arguments, repositoryPath));
        output.EnsureSuccess();
        return output.StandardOutput.Trim();
    }

    /// <summary>
    /// 只往索引里加一个文件（内容取自仓库里已有的某个文件），用于造出 Windows 工作区
    /// 建不出来的路径形态。这类提交不跑 <c>git add .</c>——全量 add 会把这类索引项剔除。
    /// </summary>
    private async Task AddIndexOnlyFileAsync(string repositoryPath, string path, string blobSourcePath)
    {
        var blobHash = await ResolveAsync(repositoryPath, $"HEAD:{blobSourcePath}");
        await TestGit.RunAsync(["update-index", "--add", "--cacheinfo", $"100644,{blobHash},{path}"], repositoryPath);
    }

    private static string TenLines(string lastLine)
        => string.Join('\n', ["line 01", "line 02", "line 03", "line 04", "line 05", "line 06", "line 07", "line 08", "line 09", lastLine])
           + "\n";

    private static async Task CommitFileAsync(string repositoryPath, string fileName, string content, string message)
    {
        var fullPath = Path.Combine(repositoryPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
    }

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
}
