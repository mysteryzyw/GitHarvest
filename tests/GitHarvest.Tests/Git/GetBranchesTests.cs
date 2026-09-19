using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 分支读取的集成测试：跑真实 git.exe，断言的是分支列表这一外部行为——
/// 本地/远程分组、当前分支标记、游离候选项置顶、每分支最新提交摘要，
/// 不关心 GitService 内部用了哪几条命令。摘要的正确性用独立的 git log 命令交叉验证。
/// </summary>
public sealed class GetBranchesTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 本地分支全部列出且仅当前分支标记IsCurrent()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "locals", commitCount: 2);
        await TestGit.RunAsync(["branch", "feature/order-export"], repositoryPath);
        await TestGit.RunAsync(["branch", "release/2.4"], repositoryPath);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        var branches = Assert.IsAssignableFrom<IReadOnlyList<BranchInfo>>(result.Branches);
        Assert.Equal(3, branches.Count);
        Assert.All(branches, branch => Assert.False(branch.IsRemote));
        Assert.All(branches, branch => Assert.False(branch.IsDetached));
        Assert.Equal(["feature/order-export", "main", "release/2.4"], branches.Select(b => b.Name).Order());
        Assert.Equal("main", Assert.Single(branches, b => b.IsCurrent).Name);
    }

    [Fact]
    public async Task 每个分支带最新提交摘要()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "tips", commitCount: 2);
        // 在另一个分支上做一次独立提交，两个分支的摘要应各自不同。
        await TestGit.RunAsync(["checkout", "-q", "-b", "feature/tip"], repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "tip.txt"), "分支上的新内容");
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "分支独有的提交"], repositoryPath);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        foreach (var branch in result.Branches!)
        {
            var expected = await ReadTipAsync(repositoryPath, branch.Name);
            Assert.Equal(expected, branch.Tip);
        }
    }

    [Fact]
    public async Task 远程跟踪分支标记IsRemote且符号引用被排除()
    {
        var originPath = await TestRepository.CreateAsync(_directory.Path, "origin.git", commitCount: 2, bare: true);
        var clonePath = Path.Combine(_directory.Path, "clone");
        await TestGit.RunAsync(["clone", "-q", originPath, clonePath]);

        var result = await CreateService().GetBranchesAsync(clonePath);

        Assert.True(result.IsSuccess);
        var remote = Assert.Single(result.Branches!, branch => branch.IsRemote);
        Assert.Equal("origin/main", remote.Name);
        // clone 会同时建立 origin/HEAD 符号引用，它不是真实分支，必须被排除。
        Assert.DoesNotContain(result.Branches!, branch => branch.Name.EndsWith("/HEAD", StringComparison.Ordinal));
        Assert.Single(result.Branches!, branch => branch is { IsRemote: false, Name: "main" });
    }

    [Fact]
    public async Task 游离头指针时列表顶部有游离候选项()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "detached", commitCount: 2);
        await TestGit.RunAsync(["checkout", "-q", "--detach", "HEAD~1"], repositoryPath);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        var detached = result.Branches![0];
        Assert.True(detached.IsDetached);
        Assert.False(detached.IsCurrent);
        Assert.False(detached.IsRemote);
        Assert.Equal("HEAD", detached.Name);
        // 游离项的摘要就是 HEAD 指向的提交。
        Assert.Equal(await ReadTipAsync(repositoryPath, "HEAD"), detached.Tip);
        // 其余本地分支照常列出，且没有一个被标成当前分支。
        Assert.Contains(result.Branches!, branch => branch is { IsDetached: false, Name: "main" });
        Assert.DoesNotContain(result.Branches!, branch => branch.IsCurrent);
    }

    [Fact]
    public async Task 非游离时没有游离候选项()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "attached", commitCount: 1);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Branches!, branch => branch.IsDetached);
    }

    [Fact]
    public async Task 空仓库返回空列表而不是失败()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "empty", commitCount: 0);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Branches!);
    }

    [Fact]
    public async Task 中文分支名与提交信息可正确读取()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "unicode", commitCount: 1);
        await TestGit.RunAsync(["checkout", "-q", "-b", "特性/订单导出"], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "--allow-empty", "-m", "修复导出乱码"], repositoryPath);

        var result = await CreateService().GetBranchesAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        var branch = Assert.Single(result.Branches!, item => item.Name == "特性/订单导出");
        Assert.True(branch.IsCurrent);
        Assert.Equal("修复导出乱码", branch.Tip.Subject);
    }

    [Fact]
    public async Task 仓库目录不存在时提示目录不存在()
    {
        var missing = Path.Combine(_directory.Path, "已被删除的仓库");

        var result = await CreateService().GetBranchesAsync(missing);

        Assert.False(result.IsSuccess);
        Assert.Equal(BranchListFailure.DirectoryNotFound, result.Failure);
        Assert.Contains("不存在", result.FailureMessage);
    }

    [Fact]
    public async Task git不可用时返回不可用失败而不是抛出()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "normal", commitCount: 1);
        var service = new GitService(StubGitEnvironment.NotRunnable(), new GitCliRunner(), SilentLogger);

        var result = await service.GetBranchesAsync(repositoryPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(BranchListFailure.GitUnavailable, result.Failure);
    }

    [Fact]
    public async Task 取消令牌已取消时抛出取消异常()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "normal", commitCount: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().GetBranchesAsync(repositoryPath, new CancellationToken(canceled: true)));
    }

    public void Dispose() => _directory.Dispose();

    private static GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    /// <summary>
    /// 用独立的 git log 命令取某引用最新提交的真实摘要，与解析结果交叉验证
    /// （真值来源与被测链路无关，字段错位或张冠李戴都会被发现）。
    /// </summary>
    private static async Task<CommitSummary> ReadTipAsync(string repositoryPath, string reference)
    {
        var result = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["log", "-1", "--format=%h%x00%s%x00%an%x00%aI", reference], repositoryPath));
        result.EnsureSuccess();

        var fields = result.StandardOutput.TrimEnd('\n').Split('\0');
        return new CommitSummary(fields[0], fields[1], fields[2], DateTimeOffset.Parse(fields[3]));
    }
}
