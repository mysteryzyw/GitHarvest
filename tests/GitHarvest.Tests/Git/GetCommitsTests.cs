using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 提交枚举（含搜索过滤）的集成测试：跑真实 git.exe，断言的是外部行为——
/// 分页切片、是否有下一页、合并提交在列、按哈希/提交信息/作者的搜索结果，
/// 不关心 GitService 内部把过滤下推给了 git 还是自己在内存里做。
/// 顺序与数量用独立的 git log 命令交叉验证。
/// </summary>
public sealed class GetCommitsTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 无搜索时按分页枚举且新提交在前()
    {
        var repositoryPath = await CreateRepositoryAsync("paged", commitCount: 5);

        var firstPage = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: null, Offset: 0, Limit: 3));
        var secondPage = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: null, Offset: 3, Limit: 3));

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Commits!.Count);
        Assert.True(firstPage.HasMore);
        // git log 默认新提交在前：第一页是第 5、4、3 个提交。
        Assert.Equal(["第 5 个提交", "第 4 个提交", "第 3 个提交"], firstPage.Commits.Select(c => c.Subject));

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(2, secondPage.Commits!.Count);
        Assert.False(secondPage.HasMore);
        Assert.Equal(["第 2 个提交", "第 1 个提交"], secondPage.Commits.Select(c => c.Subject));
    }

    [Fact]
    public async Task 分页拼接与gitLog全序一致()
    {
        var repositoryPath = await CreateRepositoryAsync("ordered", commitCount: 7);
        var expected = await ReadLogSubjectsAsync(repositoryPath, "main");

        var service = CreateService();
        var combined = new List<CommitSummary>();
        var offset = 0;
        while (true)
        {
            var page = await service.GetCommitsAsync(
                repositoryPath, "main", new CommitQuery(Search: null, Offset: offset, Limit: 3));
            Assert.True(page.IsSuccess);
            combined.AddRange(page.Commits!);
            if (!page.HasMore)
            {
                break;
            }

            offset += page.Commits!.Count;
        }

        Assert.Equal(expected, combined.Select(c => c.Subject));
    }

    [Fact]
    public async Task 合并提交出现在列表中()
    {
        var repositoryPath = await CreateRepositoryAsync("merge", commitCount: 2);
        // 分支上提交一次，回到 main 再提交一次，然后合并——产生一个真实的合并提交。
        await TestGit.RunAsync(["checkout", "-q", "-b", "feature/merge-me"], repositoryPath);
        await CommitFileAsync(repositoryPath, "feature.txt", "分支内容", "feat: 分支上的新增");
        await TestGit.RunAsync(["checkout", "-q", "main"], repositoryPath);
        await CommitFileAsync(repositoryPath, "main.txt", "主干内容", "fix: 主干上的修复");
        await TestGit.RunAsync(["merge", "-q", "--no-ff", "-m", "merge: 合并功能分支", "feature/merge-me"], repositoryPath);

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: null, Offset: 0, Limit: 20));

        Assert.True(result.IsSuccess);
        // main 的两个提交 + 分支提交 + 合并提交 = 5；列表最前是合并提交。
        Assert.Equal(5, result.Commits!.Count);
        Assert.Equal("merge: 合并功能分支", result.Commits![0].Subject);
    }

    [Fact]
    public async Task 按提交信息搜索命中包含关键词的提交()
    {
        var repositoryPath = await CreateRepositoryAsync("search-msg", commitCount: 2);
        await CommitFileAsync(repositoryPath, "login.txt", "登录逻辑", "fix: 修复登录页崩溃");

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "登录页", Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        var commit = Assert.Single(result.Commits!);
        Assert.Equal("fix: 修复登录页崩溃", commit.Subject);
    }

    [Fact]
    public async Task 搜索对英文大小写不敏感()
    {
        var repositoryPath = await CreateRepositoryAsync("search-case", commitCount: 2);
        await CommitFileAsync(repositoryPath, "alpha.txt", "内容", "feat: add Feature-Alpha module");

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "FEATURE-ALPHA", Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "feat: add Feature-Alpha module",
            Assert.Single(result.Commits!).Subject);
    }

    [Fact]
    public async Task 按作者搜索命中该作者的提交()
    {
        var repositoryPath = await CreateRepositoryAsync("search-author", commitCount: 2);
        // 用 -c 覆写本次提交的作者，与默认作者区分开。
        await CommitFileAsync(
            repositoryPath, "handover.txt", "交接内容", "docs: 模块交接说明",
            authorName: "交接人王小明", authorEmail: "wangxm@example.com");

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "交接人王小明", Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "docs: 模块交接说明",
            Assert.Single(result.Commits!).Subject);
    }

    [Fact]
    public async Task 按短哈希搜索返回对应提交()
    {
        var repositoryPath = await CreateRepositoryAsync("search-hash", commitCount: 3);
        var headHash = (await ReadGitOutputAsync(repositoryPath, ["rev-parse", "HEAD"])).Trim();

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: headHash[..7], Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "第 3 个提交",
            Assert.Single(result.Commits!).Subject);
    }

    [Fact]
    public async Task 搜索词含正则特殊字符时按字面匹配()
    {
        var repositoryPath = await CreateRepositoryAsync("search-literal", commitCount: 2);
        await CommitFileAsync(repositoryPath, "marker.txt", "内容", "chore: 标记 a{2} 占位");

        // a{2} 作为正则是「两个 a」，信息里只有字面 a{2}——只有按字面搜索才能命中。
        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "a{2}", Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "chore: 标记 a{2} 占位",
            Assert.Single(result.Commits!).Subject);
    }

    [Fact]
    public async Task 搜索在过滤后的结果集上分页()
    {
        var repositoryPath = await CreateRepositoryAsync("search-paged", commitCount: 2);
        await CommitFileAsync(repositoryPath, "a1.txt", "内容一", "feat: 接口改造第一批");
        await CommitFileAsync(repositoryPath, "a2.txt", "内容二", "feat: 接口改造第二批");
        await CommitFileAsync(repositoryPath, "a3.txt", "内容三", "feat: 接口改造第三批");

        var firstPage = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "接口改造", Offset: 0, Limit: 2));
        var secondPage = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "接口改造", Offset: 2, Limit: 2));

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(["feat: 接口改造第三批", "feat: 接口改造第二批"], firstPage.Commits!.Select(c => c.Subject));
        Assert.True(firstPage.HasMore);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(["feat: 接口改造第一批"], secondPage.Commits!.Select(c => c.Subject));
        Assert.False(secondPage.HasMore);
    }

    [Fact]
    public async Task 搜索无结果返回空列表而非失败()
    {
        var repositoryPath = await CreateRepositoryAsync("search-none", commitCount: 2);

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "main", new CommitQuery(Search: "仓库里不存在的词xyz", Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Commits!);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task 未知引用返回UnknownReference失败()
    {
        var repositoryPath = await CreateRepositoryAsync("bad-ref", commitCount: 2);

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "no-such-branch", new CommitQuery(Search: null, Offset: 0, Limit: 10));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
        Assert.Contains("no-such-branch", result.FailureMessage);
    }

    [Fact]
    public async Task 游离头指针可按HEAD引用枚举()
    {
        var repositoryPath = await CreateRepositoryAsync("detached", commitCount: 3);
        await TestGit.RunAsync(["checkout", "-q", "--detach", "HEAD~1"], repositoryPath);

        var result = await CreateService().GetCommitsAsync(
            repositoryPath, "HEAD", new CommitQuery(Search: null, Offset: 0, Limit: 10));

        Assert.True(result.IsSuccess);
        // 游离在 HEAD~1：可见第 2、1 个提交，看不到第 3 个。
        Assert.Equal(["第 2 个提交", "第 1 个提交"], result.Commits!.Select(c => c.Subject));
    }

    [Fact]
    public async Task 仓库目录不存在返回DirectoryNotFound()
    {
        var result = await CreateService().GetCommitsAsync(
            @"Z:\不存在的仓库目录", "main", new CommitQuery(Search: null, Offset: 0, Limit: 10));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.DirectoryNotFound, result.Failure);
    }

    [Fact]
    public async Task 取消令牌已取消时抛出取消异常()
    {
        var repositoryPath = await CreateRepositoryAsync("canceled", commitCount: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().GetCommitsAsync(
                repositoryPath, "main", new CommitQuery(Search: null, Offset: 0, Limit: 10), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task 数千提交的仓库分页枚举保持正确()
    {
        // 验收场景「含数千提交的 fixture 仓库」：git fast-import 一次进程造 2000 个提交，
        // 验证分页遍历全序与 git log 一致、HasMore 在最后一页正确收尾——
        // 界面滚动流畅性由 ListBox 虚拟化 + 本分页语义保证，交互手感属实机验证。
        const int commitCount = 2000;
        var repositoryPath = Path.Combine(_directory.Path, "thousands");
        Directory.CreateDirectory(repositoryPath);
        await TestGit.RunAsync(["init", "-q", "-b", "main", repositoryPath]);

        var builder = new System.Text.StringBuilder();
        for (var index = 1; index <= commitCount; index++)
        {
            var message = $"第 {index:D4} 个提交";
            // fast-import 的 data 长度按 UTF-8 字节计（中文一条占 3 字节），按字符数会导致解析失败。
            var messageBytes = System.Text.Encoding.UTF8.GetByteCount(message);
            builder.Append("commit refs/heads/main\n");
            builder.Append($"mark :{index}\n");
            builder.Append("author 交付测试 <test@githarvest.local> 1690000000 +0800\n");
            builder.Append("committer 交付测试 <test@githarvest.local> 1690000000 +0800\n");
            builder.Append($"data {messageBytes}\n{message}\n");
        }

        await TestGit.RunWithStdinAsync(["fast-import", "--quiet"], builder.ToString(), repositoryPath);

        var expected = await ReadLogSubjectsAsync(repositoryPath, "main");
        Assert.Equal(commitCount, expected.Count);

        var service = CreateService();
        var combined = new List<CommitSummary>();
        var offset = 0;
        while (true)
        {
            var page = await service.GetCommitsAsync(
                repositoryPath, "main", new CommitQuery(Search: null, Offset: offset, Limit: 100));
            Assert.True(page.IsSuccess);
            Assert.True(page.Commits!.Count <= 100);
            combined.AddRange(page.Commits);
            if (!page.HasMore)
            {
                break;
            }

            offset += page.Commits.Count;
        }

        Assert.Equal(commitCount, combined.Count);
        Assert.Equal(expected, combined.Select(c => c.Subject));
    }

    public void Dispose() => _directory.Dispose();

    private GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount, bool bare = false)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount, bare);

    /// <summary>追加一次带自定义作者（可选）的文件提交。</summary>
    private static async Task CommitFileAsync(
        string repositoryPath,
        string fileName,
        string content,
        string message,
        string? authorName = null,
        string? authorEmail = null)
    {
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, fileName), content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        if (authorName is not null && authorEmail is not null)
        {
            await TestGit.RunAsync(
                ["-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}", "commit", "-q", "-m", message],
                repositoryPath);
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

    /// <summary>用独立的 git log 读取全部提交信息首行（新→旧），作为期望顺序。</summary>
    private async Task<IReadOnlyList<string>> ReadLogSubjectsAsync(string repositoryPath, string reference)
    {
        var output = await ReadGitOutputAsync(repositoryPath, ["log", "--format=%s", reference]);
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }
}
