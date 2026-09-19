using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 打开仓库的集成测试：跑真实 git.exe，在临时目录里造出各种形态的仓库
/// （普通 / bare / 空 / 单提交 / 游离头指针 / 非仓库），断言的是打开结果这一外部行为——
/// 仓库根、bare 标记、当前分支、提交数与失败提示，不关心 GitService 内部用了哪几条命令。
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 打开普通仓库返回仓库根当前分支与提交数()
    {
        var repositoryPath = await CreateRepositoryAsync("normal", commitCount: 3);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        var repository = Assert.IsType<RepositoryInfo>(result.Repository);
        Assert.False(repository.IsBare);
        Assert.Equal("main", repository.CurrentBranch);
        Assert.Equal(3, repository.CommitCount);
        // 仓库根应等于造出来的仓库目录（git 的正斜杠输出已由实现归一化，这里再忽略大小写兜底）。
        Assert.Equal(TestPaths.Normalize(repositoryPath), TestPaths.Normalize(repository.RootPath), ignoreCase: true);
    }

    [Fact]
    public async Task 从仓库子目录打开时解析出仓库真正的根()
    {
        var repositoryPath = await CreateRepositoryAsync("normal", commitCount: 1);
        var subdirectory = Path.Combine(repositoryPath, "sub", "deep");
        Directory.CreateDirectory(subdirectory);

        var result = await CreateService().OpenRepositoryAsync(subdirectory);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestPaths.Normalize(repositoryPath), TestPaths.Normalize(result.Repository!.RootPath), ignoreCase: true);
    }

    [Fact]
    public async Task 打开bare仓库成功且标记为bare()
    {
        var repositoryPath = await CreateRepositoryAsync("bare.git", commitCount: 2, bare: true);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.True(result.Repository!.IsBare);
        // bare 仓库的 HEAD 符号引用仍指向一个分支，仓库根就是仓库目录本身。
        Assert.Equal("main", result.Repository.CurrentBranch);
        Assert.Equal(TestPaths.Normalize(repositoryPath), TestPaths.Normalize(result.Repository.RootPath), ignoreCase: true);
    }

    [Fact]
    public async Task 打开非仓库目录时提示不是Git仓库()
    {
        var notARepository = Path.Combine(_directory.Path, "not-repo");
        Directory.CreateDirectory(notARepository);

        var result = await CreateService().OpenRepositoryAsync(notARepository);

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryOpenFailure.NotARepository, result.Failure);
        Assert.Contains("不是 Git 仓库", result.FailureMessage);
        Assert.Null(result.Repository);
    }

    [Fact]
    public async Task 打开不存在的目录时提示目录不存在()
    {
        var missingDirectory = Path.Combine(_directory.Path, "已被删除的仓库");

        var result = await CreateService().OpenRepositoryAsync(missingDirectory);

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryOpenFailure.DirectoryNotFound, result.Failure);
        Assert.Contains("不存在", result.FailureMessage);
    }

    [Fact]
    public async Task 空仓库打开成功且提交数为零不足以构成变更范围()
    {
        var repositoryPath = await CreateRepositoryAsync("empty", commitCount: 0);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Repository!.CommitCount);
        Assert.False(result.Repository.HasEnoughCommits);
    }

    [Fact]
    public async Task 单提交仓库不足以构成变更范围()
    {
        var repositoryPath = await CreateRepositoryAsync("single", commitCount: 1);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Repository!.CommitCount);
        Assert.False(result.Repository.HasEnoughCommits);
    }

    [Fact]
    public async Task 游离头指针时当前分支为空()
    {
        var repositoryPath = await CreateRepositoryAsync("detached", commitCount: 2);
        await TestGit.RunAsync(["checkout", "-q", "--detach", "HEAD~1"], repositoryPath);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Repository!.CurrentBranch);
    }

    [Fact]
    public async Task git不可用时返回不可用失败而不是抛出()
    {
        var repositoryPath = await CreateRepositoryAsync("normal", commitCount: 1);
        var service = new GitService(
            new StubGitEnvironment(GitEnvironmentStatus.NotRunnable()),
            new GitCliRunner(),
            SilentLogger);

        var result = await service.OpenRepositoryAsync(repositoryPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryOpenFailure.GitUnavailable, result.Failure);
    }

    [Fact]
    public async Task 中文路径的仓库可正常打开()
    {
        var repositoryPath = await CreateRepositoryAsync("交付仓库-示例", commitCount: 1);

        var result = await CreateService().OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestPaths.Normalize(repositoryPath), TestPaths.Normalize(result.Repository!.RootPath), ignoreCase: true);
    }

    [Fact]
    public async Task 取消令牌已取消时抛出取消异常()
    {
        var repositoryPath = await CreateRepositoryAsync("normal", commitCount: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().OpenRepositoryAsync(repositoryPath, new CancellationToken(canceled: true)));
    }

    public void Dispose() => _directory.Dispose();

    /// <summary>用本机真实 git.exe 构造被测服务（环境自检桩直接给出可用状态，不走探测）。</summary>
    private static GitService CreateService() => new(
        new StubGitEnvironment(
            GitEnvironmentStatus.Available(TestGit.ExecutablePath, "test", GitExecutableSource.PathEnvironment)),
        new GitCliRunner(),
        SilentLogger);

    /// <summary>在测试目录下造一个形态可配的仓库（共享辅助见 <see cref="TestRepository"/>）。</summary>
    private Task<string> CreateRepositoryAsync(string name, int commitCount, bool bare = false)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount, bare);

    /// <summary>环境自检桩：跳过探测直接返回给定状态，让 GitService 拿到本机真实 git.exe 的路径。</summary>
    private sealed class StubGitEnvironment(GitEnvironmentStatus status) : IGitEnvironmentService
    {
        public Task<GitEnvironmentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(status);

        public Task<GitEnvironmentStatus> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(status);
    }
}
