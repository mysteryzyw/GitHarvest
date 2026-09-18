using GitHarvest.Core.Git;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Settings;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// git 环境自检的外部行为：按「手动指定 → PATH → 常见安装路径」的顺序逐个用 <c>git --version</c> 验证，
/// 首个可用者即结果；全部失败时给出可执行的中文引导；结果被缓存，重新探测会重读设置。
/// 这里只经 IGitEnvironmentService 观察，不碰内部实现。
/// </summary>
public class GitEnvironmentServiceTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 自动探测到本机git并给出版本与来源()
    {
        var service = CreateService(GitExecutableLocator.ForCurrentEnvironment());

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.True(File.Exists(status.ExecutablePath));
        Assert.True(status.Source is GitExecutableSource.PathEnvironment or GitExecutableSource.CommonInstallPath);
        // 版本号取自 `git version 2.55.0.windows.3` 这类输出，应当只剩版本本身。
        Assert.DoesNotContain("git version", status.Version);
        Assert.Matches(@"^\d+\.", status.Version!);
        Assert.Null(status.Guidance);
    }

    [Fact]
    public async Task 三级都没找到时给出安装引导()
    {
        var service = CreateService(new GitExecutableLocator([], []));

        var status = await service.GetStatusAsync();

        Assert.False(status.IsAvailable);
        Assert.Equal(GitUnavailableReason.NotFound, status.Reason);
        Assert.Null(status.ExecutablePath);
        Assert.Contains("Git for Windows", status.Guidance);
        Assert.Contains("全局设置", status.Guidance);
    }

    [Fact]
    public async Task 找到的git都不可执行时给出手动指定路径的引导()
    {
        var brokenExecutable = TestGit.CreateUnrunnable(Path.Combine(_directory.Path, "on-path"));
        var service = CreateService(
            new GitExecutableLocator([Path.GetDirectoryName(brokenExecutable)!], []));

        var status = await service.GetStatusAsync();

        Assert.False(status.IsAvailable);
        Assert.Equal(GitUnavailableReason.NotRunnable, status.Reason);
        Assert.Contains("全局设置", status.Guidance);
    }

    [Fact]
    public async Task 设置中手动指定的可用路径优先于自动探测()
    {
        // 用真实 SettingsService 保存手动路径（ticket 03 起设置走内存快照 + 落盘）
        var settings = CreateSettingsService();
        settings.Save(new GlobalSettings { GitExecutablePath = TestGit.ExecutablePath });
        var service = CreateService(GitExecutableLocator.ForCurrentEnvironment(), settings);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.Equal(GitExecutableSource.ManualSetting, status.Source);
        Assert.Equal(Path.GetFullPath(TestGit.ExecutablePath), status.ExecutablePath);
    }

    [Fact]
    public async Task 设置中手动路径不可用时回退到自动探测()
    {
        var brokenExecutable = TestGit.CreateUnrunnable(Path.Combine(_directory.Path, "manual"));
        var settings = CreateSettingsService();
        settings.Save(new GlobalSettings { GitExecutablePath = brokenExecutable });
        var service = CreateService(GitExecutableLocator.ForCurrentEnvironment(), settings);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.NotEqual(GitExecutableSource.ManualSetting, status.Source);
        Assert.NotEqual(brokenExecutable, status.ExecutablePath);
    }

    [Fact]
    public async Task 自检结果被缓存()
    {
        var service = CreateService(GitExecutableLocator.ForCurrentEnvironment());

        var first = await service.GetStatusAsync();
        var second = await service.GetStatusAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task 重新探测会重读设置并反映最新状态()
    {
        // 模拟 ticket 12 的真实流程：设置页保存（ISettingsService.Save）→ 触发重新探测。
        var settings = CreateSettingsService();
        settings.Save(new GlobalSettings
        {
            GitExecutablePath = TestGit.CreateUnrunnable(Path.Combine(_directory.Path, "manual")),
        });
        var service = CreateService(new GitExecutableLocator([], []), settings);

        var before = await service.GetStatusAsync();
        settings.Save(new GlobalSettings { GitExecutablePath = TestGit.ExecutablePath });
        var after = await service.RefreshAsync();

        Assert.False(before.IsAvailable);
        Assert.True(after.IsAvailable);
        Assert.Equal(GitExecutableSource.ManualSetting, after.Source);
    }

    [Fact]
    public async Task 探测结果写入日志()
    {
        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            var service = CreateService(GitExecutableLocator.ForCurrentEnvironment(), logger: logger);

            await service.GetStatusAsync();
        }

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("git 探测成功", content);
        Assert.Contains(TestGit.ExecutablePath, content);
    }

    public void Dispose() => _directory.Dispose();

    private string SettingsFilePath => Path.Combine(_directory.Path, "settings.json");

    private GitEnvironmentService CreateService(
        GitExecutableLocator locator,
        IGitExecutablePathProvider? gitPathProvider = null,
        Serilog.ILogger? logger = null)
    {
        // 默认「设置里没配 git 路径」；需要手动路径的用例传入真实 SettingsService。
        return new GitEnvironmentService(
            locator,
            new GitCliRunner(),
            gitPathProvider ?? new NoGitExecutablePathProvider(),
            logger ?? SilentLogger);
    }

    private SettingsService CreateSettingsService(Serilog.ILogger? logger = null)
        => new(
            SettingsFilePath,
            Path.Combine(_directory.Path, "repository-state.json"),
            logger ?? SilentLogger);

    private static readonly Serilog.ILogger SilentLogger = new Serilog.LoggerConfiguration().CreateLogger();

    /// <summary>「设置里没配 git 路径」的读取器。</summary>
    private sealed class NoGitExecutablePathProvider : IGitExecutablePathProvider
    {
        public string? GitExecutablePath => null;
    }
}
