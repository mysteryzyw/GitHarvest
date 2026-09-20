using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 双点范围提交数统计的集成测试：跑真实 git.exe，断言外部行为——
/// base..head 的计数语义（含 Head、不含基准）、同一提交为 0、分叉对也能得到数量
/// （校验是另一件事，计数本身对任意两个提交都有定义）、坏哈希归类。
/// </summary>
public sealed class GetRangeCommitCountTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 计数含Head不含基准()
    {
        var repositoryPath = await CreateRepositoryAsync("count-semantic", commitCount: 5);

        var result = await CreateService().GetRangeCommitCountAsync(
            repositoryPath,
            await ResolveAsync(repositoryPath, "HEAD~2"),
            await ResolveAsync(repositoryPath, "HEAD"));

        Assert.True(result.IsSuccess);
        // HEAD~2..HEAD 含 HEAD 本身、不含基准：第 5、4 两个提交。
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task 基准与Head为同一提交时计数为零()
    {
        var repositoryPath = await CreateRepositoryAsync("count-same", commitCount: 2);
        var hash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetRangeCommitCountAsync(repositoryPath, hash, hash);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task 分叉提交对也能得到数量()
    {
        var repositoryPath = await CreateRepositoryAsync("count-forked", commitCount: 1);
        await TestGit.RunAsync(["checkout", "-q", "-b", "chain-a"], repositoryPath);
        await CommitFileAsync(repositoryPath, "a.txt", "a", "链 A");
        var tipA = await ResolveAsync(repositoryPath, "HEAD");
        await TestGit.RunAsync(["checkout", "-q", "-b", "chain-b", "main"], repositoryPath);
        await CommitFileAsync(repositoryPath, "b.txt", "b", "链 B");
        var tipB = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetRangeCommitCountAsync(repositoryPath, tipB, tipA);

        // 计数对分叉对同样有定义：tipA 可达而 tipB 不可达的提交 = 链 A 在分叉点后的 1 个提交。
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task 引用不存在时返回UnknownReference()
    {
        var repositoryPath = await CreateRepositoryAsync("count-bad-ref", commitCount: 2);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetRangeCommitCountAsync(
            repositoryPath, "ffffffffffffffffffffffffffffffffffffffff", headHash);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
    }

    [Fact]
    public async Task 仓库目录不存在时返回DirectoryNotFound()
    {
        var result = await CreateService().GetRangeCommitCountAsync(
            @"Z:\不存在仓库", "aaa", "bbb");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.DirectoryNotFound, result.Failure);
    }

    [Fact]
    public async Task Git不可用时返回GitUnavailable()
    {
        var service = new GitService(StubGitEnvironment.NotRunnable(), new GitCliRunner(), SilentLogger);
        var repositoryPath = await CreateRepositoryAsync("count-no-git", commitCount: 1);

        var result = await service.GetRangeCommitCountAsync(repositoryPath, "aaa", "bbb");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.GitUnavailable, result.Failure);
    }

    public void Dispose() => _directory.Dispose();

    private GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount = 0, bool bare = false)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount, bare);

    /// <summary>用 rev-parse 解析引用为完整哈希（期望值的交叉验证基准）。</summary>
    private async Task<string> ResolveAsync(string repositoryPath, string reference)
    {
        var output = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["rev-parse", reference], repositoryPath));
        output.EnsureSuccess();
        return output.StandardOutput.Trim();
    }

    /// <summary>追加一次文件提交（写文件 → add → commit）。</summary>
    private static async Task CommitFileAsync(string repositoryPath, string fileName, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, fileName), content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
    }
}
