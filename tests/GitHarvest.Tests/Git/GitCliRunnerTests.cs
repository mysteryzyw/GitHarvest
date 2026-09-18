using System.Diagnostics;
using System.Text;
using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// git CLI 调用封装：LC_ALL=C 锁定输出语言、UTF-8 解码中文、-z 空字符分隔的记录切分、
/// 退出码与 stderr 集中处理、异步与取消令牌。
/// 这些用例跑真实 git.exe；本机没装 Git 时由 <see cref="TestGit"/> 明确失败。
/// </summary>
public class GitCliRunnerTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly GitCliRunner _runner = new();

    [Fact]
    public async Task 执行版本命令拿到标准输出与零退出码()
    {
        var result = await _runner.RunAsync(TestGit.Invocation(["--version"]));

        Assert.True(result.IsSuccess);
        Assert.StartsWith("git version", result.StandardOutput.Trim());
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task 标准输出按UTF8解码中文内容()
    {
        var configFile = WriteConfigFile("[user]\n\tname = 张三\n");

        var result = await _runner.RunAsync(TestGit.Invocation(["config", "--file", configFile, "--get", "user.name"]));

        Assert.True(result.IsSuccess);
        Assert.Equal("张三", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task 零分隔输出按空字符切成记录()
    {
        // -z 让 git 用空字符分隔记录，同时关掉非 ASCII 路径的转义（这正是本项目约定 -z 的原因）。
        var configFile = WriteConfigFile("[user]\n\tname = 张三\n[color]\n\tui = always\n");

        var result = await _runner.RunAsync(TestGit.Invocation(["config", "--file", configFile, "--list", "-z"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(["user.name\n张三", "color.ui\nalways"], result.GetNulSeparatedRecords());
    }

    [Fact]
    public async Task 命令失败时保留退出码与英文标准错误()
    {
        // 工作目录不是仓库，git 必然失败；LC_ALL=C 保证 stderr 是英文，这里正好把它一并断言。
        var result = await _runner.RunAsync(
            TestGit.Invocation(["rev-parse", "--git-dir"], workingDirectory: _directory.Path));

        Assert.False(result.IsSuccess);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a git repository", result.StandardError);
    }

    [Fact]
    public async Task 命令失败时集中抛出带标准错误的异常()
    {
        var result = await _runner.RunAsync(
            TestGit.Invocation(["rev-parse", "--git-dir"], workingDirectory: _directory.Path));

        var exception = Assert.Throws<GitCommandException>(() => result.EnsureSuccess());

        Assert.Contains("not a git repository", exception.Message);
        Assert.Equal(result, exception.Result);
    }

    [Fact]
    public async Task 无法启动的可执行文件抛出带路径的异常()
    {
        // 造一个同名但不是可执行文件的 git.exe：启动会失败，调用方应拿到明确的异常而不是空结果。
        var fakeExecutable = TestGit.CreateUnrunnable(_directory.Path);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => _runner.RunAsync(new GitInvocation(fakeExecutable, ["--version"])));

        Assert.Contains(fakeExecutable, exception.Message);
    }

    [Fact]
    public async Task 已取消的令牌不会启动进程()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync(new GitInvocation("请勿启动的可执行文件", ["--version"]), cancellation.Token));
    }

    [Fact]
    public async Task 取消令牌在进程运行中生效并终止进程()
    {
        // git 没有可控的长跑子命令，这里用本机 ping（30 次约 30 秒）当长跑替身；
        // 断言「及时返回」即证明进程被终止，而不是等它自然结束。
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync(
                new GitInvocation(
                    Path.Combine(Environment.SystemDirectory, "ping.exe"),
                    ["-n", "30", "127.0.0.1"]),
                cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"取消后应立即返回，实际耗时 {stopwatch.Elapsed}。");
    }

    public void Dispose() => _directory.Dispose();

    private string WriteConfigFile(string content)
    {
        var path = Path.Combine(_directory.Path, "config");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
