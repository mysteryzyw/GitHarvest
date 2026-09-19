using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Support;

/// <summary>
/// 环境自检桩：跳过探测直接返回给定状态，让被测服务拿到本机真实 git.exe 的路径
/// （或一个不可用状态）。各 Git 集成测试类共用的接缝桩。
/// </summary>
internal sealed class StubGitEnvironment(GitEnvironmentStatus status) : IGitEnvironmentService
{
    /// <summary>「本机 git 可用」的桩（路径为真实 git.exe，来源与版本不参与断言）。</summary>
    public static StubGitEnvironment Available()
        => new(GitEnvironmentStatus.Available(TestGit.ExecutablePath, "test", GitExecutableSource.PathEnvironment));

    /// <summary>「git 不可用」的桩（有候选但都跑不起来）。</summary>
    public static StubGitEnvironment NotRunnable()
        => new(GitEnvironmentStatus.NotRunnable());

    public Task<GitEnvironmentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(status);

    public Task<GitEnvironmentStatus> RefreshAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(status);
}
