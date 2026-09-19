using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 「拉取」（git fetch）的集成测试：远程是本地 bare 仓库（不需要网络与凭据），
/// 断言的是外部行为——远程跟踪引用被更新、工作区与当前分支不受影响、
/// 无远程时得到明确提示，不关心 fetch 的具体输出。
/// </summary>
public sealed class FetchTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 无远程的仓库拉取返回无远程结果()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "local-only", commitCount: 1);

        var result = await CreateService().FetchAsync(repositoryPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(FetchFailure.NoRemote, result.Failure);
        Assert.Contains("远程", result.FailureMessage);
    }

    [Fact]
    public async Task fetch后远程跟踪引用更新到新提交()
    {
        // 场景：origin（本地 bare）上多了一个新提交，clone 里的 origin/main 还停在旧位置。
        var originPath = await TestRepository.CreateAsync(_directory.Path, "origin.git", commitCount: 1, bare: true);
        var clonePath = Path.Combine(_directory.Path, "clone");
        await TestGit.RunAsync(["clone", "-q", originPath, clonePath]);

        // 另开一个工作仓库向 origin 推新提交（模拟别人推上去的更新）。
        var pusherPath = Path.Combine(_directory.Path, "pusher");
        await TestGit.RunAsync(["clone", "-q", originPath, pusherPath]);
        await File.WriteAllTextAsync(Path.Combine(pusherPath, "pushed.txt"), "别人推上来的内容");
        await TestGit.RunAsync(["add", "."], pusherPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "远程新增的提交"], pusherPath);
        await TestGit.RunAsync(["push", "-q", "origin", "main"], pusherPath);

        var fetch = await CreateService().FetchAsync(clonePath);

        Assert.True(fetch.IsSuccess);
        // fetch 本身不返回分支数据，刷新由调用方重读完成——这里按同一方式验证引用已更新。
        var branches = await CreateService().GetBranchesAsync(clonePath);
        var remoteMain = Assert.Single(branches.Branches!, branch => branch.Name == "origin/main");
        Assert.Equal("远程新增的提交", remoteMain.Tip.Subject);
    }

    [Fact]
    public async Task fetch不动工作区与当前分支()
    {
        var originPath = await TestRepository.CreateAsync(_directory.Path, "origin.git", commitCount: 1, bare: true);
        var clonePath = Path.Combine(_directory.Path, "clone");
        await TestGit.RunAsync(["clone", "-q", originPath, clonePath]);

        // 工作区留下未提交改动、切到一个本地分支，记下 HEAD。
        await File.WriteAllTextAsync(Path.Combine(clonePath, "dirty.txt"), "未提交的改动");
        await TestGit.RunAsync(["checkout", "-q", "-b", "work"], clonePath);
        var headBefore = await ReadHeadAsync(clonePath);

        var result = await CreateService().FetchAsync(clonePath);

        Assert.True(result.IsSuccess);
        Assert.Equal("未提交的改动", await File.ReadAllTextAsync(Path.Combine(clonePath, "dirty.txt")));
        Assert.Equal(headBefore, await ReadHeadAsync(clonePath));
        var branch = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["branch", "--show-current"], clonePath));
        Assert.Equal("work", branch.StandardOutput.Trim());
    }

    [Fact]
    public async Task 仓库目录不存在时提示目录不存在()
    {
        var missing = Path.Combine(_directory.Path, "已被删除的仓库");

        var result = await CreateService().FetchAsync(missing);

        Assert.False(result.IsSuccess);
        Assert.Equal(FetchFailure.DirectoryNotFound, result.Failure);
        Assert.Contains("不存在", result.FailureMessage);
    }

    [Fact]
    public async Task git不可用时返回不可用失败而不是抛出()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "normal", commitCount: 1);
        var service = new GitService(StubGitEnvironment.NotRunnable(), new GitCliRunner(), SilentLogger);

        var result = await service.FetchAsync(repositoryPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(FetchFailure.GitUnavailable, result.Failure);
    }

    [Fact]
    public async Task 取消令牌已取消时抛出取消异常()
    {
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "normal", commitCount: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().FetchAsync(repositoryPath, new CancellationToken(canceled: true)));
    }

    public void Dispose() => _directory.Dispose();

    private static GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    /// <summary>取当前 HEAD 的完整哈希，用于验证 fetch 没有移动工作区所在位置。</summary>
    private static async Task<string> ReadHeadAsync(string repositoryPath)
    {
        var result = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["rev-parse", "HEAD"], repositoryPath));
        result.EnsureSuccess();
        return result.StandardOutput.Trim();
    }
}
