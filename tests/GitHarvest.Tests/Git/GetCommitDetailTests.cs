using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 单提交详情（完整信息、父提交、diffstat）的集成测试：跑真实 git.exe，
/// 断言的是外部行为。数字与文本都用独立的 git 命令（rev-parse / log / diff --numstat）
/// 交叉验证，不手工硬编码期望值，避免测试现场与断言同时写错。
/// </summary>
public sealed class GetCommitDetailTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 返回完整哈希短哈希与多行提交信息()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-msg", commitCount: 2);
        // git 的 -m 两段：第一段是标题，第二段是正文（隔一空行）。
        await CommitFileAsync(repositoryPath, "note.txt", "内容", "fix: 修复导出缺陷", body: "问题出在路径拼接：\n分隔符按硬编码处理了。");

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!;
        Assert.Equal((await ReadGitOutputAsync(repositoryPath, ["rev-parse", "HEAD"])).Trim(), detail.Hash);
        Assert.Equal("fix: 修复导出缺陷", detail.Subject);
        // %B 是完整信息：标题在前，正文跟在空行之后；两段都要在。
        Assert.Contains("fix: 修复导出缺陷", detail.FullMessage);
        Assert.Contains("问题出在路径拼接：", detail.FullMessage);
        Assert.Contains("分隔符按硬编码处理了。", detail.FullMessage);
    }

    [Fact]
    public async Task 返回作者时间与父提交哈希()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-meta", commitCount: 2);
        var parentHash = (await ReadGitOutputAsync(repositoryPath, ["rev-parse", "HEAD~1"])).Trim();

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!;
        // 作者与时间用独立 git log 交叉验证，不硬编码期望值。
        var expected = await ReadLogFieldsAsync(repositoryPath, "HEAD");
        Assert.Equal(expected.AuthorName, detail.AuthorName);
        Assert.Equal(expected.AuthorTime, detail.AuthorTime);
        Assert.Equal([parentHash], detail.ParentHashes);
    }

    [Fact]
    public async Task 根提交没有父且diffstat统计全部初始文件()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-root", commitCount: 1);

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!;
        Assert.Empty(detail.ParentHashes);
        // 首提交带一个初始文件（TestRepository 每提交一个 file-N.txt），相对空树是纯新增。
        Assert.Equal(1, detail.DiffStat.FilesChanged);
        Assert.Equal(1, detail.DiffStat.Additions);
        Assert.Equal(0, detail.DiffStat.Deletions);
    }

    [Fact]
    public async Task 修改提交的diffstat统计增删行数()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-modify", commitCount: 1);
        // 修改既有文件：原内容 1 行，改成 4 行（+3 行）——包含中文与换行差异。
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "file-1.txt"),
            "第一行（修改后）\n第二行\n第三行\n第四行");
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "feat: 补充说明"], repositoryPath);

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!;
        Assert.Equal("feat: 补充说明", detail.Subject);
        Assert.Equal(1, detail.DiffStat.FilesChanged);
        Assert.Equal(4, detail.DiffStat.Additions);
        Assert.Equal(1, detail.DiffStat.Deletions);
    }

    [Fact]
    public async Task 合并提交有两个父且diffstat相对第一父()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-merge", commitCount: 1);
        await TestGit.RunAsync(["checkout", "-q", "-b", "feature/merge-me"], repositoryPath);
        await CommitFileAsync(repositoryPath, "feature.txt", "分支第一行\n分支第二行", "feat: 分支新增文件");
        await TestGit.RunAsync(["checkout", "-q", "main"], repositoryPath);
        await CommitFileAsync(repositoryPath, "main.txt", "主干内容", "fix: 主干修改");
        await TestGit.RunAsync(["merge", "-q", "--no-ff", "-m", "merge: 合并功能分支", "feature/merge-me"], repositoryPath);

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!;
        Assert.Equal(2, detail.ParentHashes.Count);
        // 第一父是合并前 main 的最新提交。
        Assert.Equal((await ReadGitOutputAsync(repositoryPath, ["rev-parse", "HEAD~1"])).Trim(), detail.ParentHashes[0]);
        // diffstat 与 git diff --numstat 第一父..合并提交 的汇总一致（交叉验证）。
        var expected = await SummarizeNumstatAsync(repositoryPath, [.. detail.ParentHashes.Take(1), detail.Hash]);
        Assert.Equal(expected, detail.DiffStat);
    }

    [Fact]
    public async Task 二进制文件计入文件数但不计增删行数()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-binary", commitCount: 1);
        await File.WriteAllBytesAsync(Path.Combine(repositoryPath, "logo.bin"), [0x00, 0x01, 0xFF, 0xFE, 0x89, 0x50]);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "chore: 增加二进制资源"], repositoryPath);

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "HEAD");

        Assert.True(result.IsSuccess);
        var detail = result.Detail!.DiffStat;
        Assert.Equal(1, detail.FilesChanged);
        Assert.Equal(0, detail.Additions);
        Assert.Equal(0, detail.Deletions);
    }

    [Fact]
    public async Task 未知哈希返回UnknownReference失败()
    {
        var repositoryPath = await CreateRepositoryAsync("detail-badhash", commitCount: 2);

        var result = await CreateService().GetCommitDetailAsync(repositoryPath, "1234567890abcdef1234567890abcdef12345678");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
    }

    [Fact]
    public async Task 仓库目录不存在返回DirectoryNotFound()
    {
        var result = await CreateService().GetCommitDetailAsync(@"Z:\不存在的仓库目录", "HEAD");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.DirectoryNotFound, result.Failure);
    }

    public void Dispose() => _directory.Dispose();

    private GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount, bool bare = false)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount, bare);

    private static async Task CommitFileAsync(
        string repositoryPath,
        string fileName,
        string content,
        string message,
        string? body = null)
    {
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, fileName), content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        if (body is not null)
        {
            await TestGit.RunAsync(["commit", "-q", "-m", message, "-m", body], repositoryPath);
        }
        else
        {
            await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
        }
    }

    private static async Task<string> ReadGitOutputAsync(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var result = await new GitCliRunner().RunAsync(TestGit.Invocation(arguments, workingDirectory));
        result.EnsureSuccess();
        return result.StandardOutput;
    }

    /// <summary>用独立的 git log 读取作者与作者时间（ISO 8601 roundtrip）。</summary>
    private async Task<(string AuthorName, DateTimeOffset AuthorTime)> ReadLogFieldsAsync(
        string repositoryPath, string reference)
    {
        var output = await ReadGitOutputAsync(repositoryPath, ["log", "-1", "--format=%an%x00%aI", reference]);
        var fields = output.TrimEnd('\n').Split('\0');
        return (
            fields[0],
            DateTimeOffset.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    /// <summary>用独立的 git diff --numstat 汇总 diffstat，作为期望值。</summary>
    private static async Task<CommitDiffStat> SummarizeNumstatAsync(string repositoryPath, IReadOnlyList<string> revisions)
    {
        var output = await ReadGitOutputAsync(repositoryPath, ["diff", "--numstat", .. revisions]);
        var filesChanged = 0;
        var additions = 0;
        var deletions = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            filesChanged++;
            if (int.TryParse(fields[0], out var added))
            {
                additions += added;
            }

            if (int.TryParse(fields[1], out var deleted))
            {
                deletions += deleted;
            }
        }

        return new CommitDiffStat(filesChanged, additions, deletions);
    }
}
