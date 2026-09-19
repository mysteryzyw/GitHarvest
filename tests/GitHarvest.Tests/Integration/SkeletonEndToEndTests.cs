using System.Text.Json;
using GitHarvest.Core.Git;
using GitHarvest.Core.Settings;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Integration;

/// <summary>
/// 骨架小组（ticket 01–04）的整组端到端验证：按应用启动的真实顺序组合 Core 服务
/// （设置加载 → git.exe 真实探测 → 打开仓库 → 最近列表记忆），把四个 ticket 串成一条链路。
/// 与 <c>GitServiceTests</c> 的细粒度覆盖不同，这里每个用例都从「启动」开始走完整链路，
/// 且探测走真实环境（<c>GitExecutableLocator.ForCurrentEnvironment()</c>，不打桩）。
/// 断言的都是外部行为：打开结果、给用户的中文提示、持久化文件内容；重启用「同一组文件
/// 路径构造新实例」模拟。
/// </summary>
public sealed class SkeletonEndToEndTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    private string SettingsFilePath => Path.Combine(_directory.Path, "data", "settings.json");

    private string RepositoryStateFilePath => Path.Combine(_directory.Path, "data", "repository-state.json");

    [Fact]
    public async Task 端到端_启动探测git后打开普通仓库并记忆最近列表()
    {
        var (settings, git, environment) = CreateSession();

        // 链路第一环：首次启动，设置文件不存在时按默认值生成（App.OnStartup 显式解析设置的等价动作）。
        Assert.True(File.Exists(SettingsFilePath));

        // 第二环：启动时真实探测 git.exe（不打桩，走 PATH → 常见安装路径）。
        var status = await environment.GetStatusAsync();
        Assert.True(status.IsAvailable);
        Assert.True(File.Exists(status.ExecutablePath));

        // 第三环：打开普通仓库成功。
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "normal", commitCount: 3);
        var result = await git.OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Equal("main", result.Repository!.CurrentBranch);
        Assert.Equal(3, result.Repository.CommitCount);
        Assert.True(result.Repository.HasEnoughCommits);

        // 第四环：打开成功进入最近列表（RepositoryViewModel 的既有行为），并落盘。
        settings.AddRecentRepository(result.Repository.RootPath);

        var recent = settings.RecentRepositories;
        Assert.Equal(TestPaths.Normalize(result.Repository.RootPath), TestPaths.Normalize(recent[0]));

        // 持久化文件内容是契约：断言 recentRepositories 字段真实写入且首条就是刚打开的仓库根。
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryStateFilePath));
        var persistedRecent = document.RootElement.GetProperty("recentRepositories");
        Assert.Equal(
            TestPaths.Normalize(result.Repository.RootPath),
            TestPaths.Normalize(persistedRecent[0].GetString()!));
    }

    [Fact]
    public async Task 端到端_打开bare仓库成功()
    {
        var (_, git, environment) = CreateSession();
        await environment.GetStatusAsync();

        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "bare.git", commitCount: 2, bare: true);
        var result = await git.OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.True(result.Repository!.IsBare);
        Assert.Equal("main", result.Repository.CurrentBranch);
        Assert.True(result.Repository.HasEnoughCommits);
    }

    [Fact]
    public async Task 端到端_打开非仓库目录得到明确错误提示()
    {
        var (_, git, environment) = CreateSession();
        await environment.GetStatusAsync();

        var notARepository = Path.Combine(_directory.Path, "not-repo");
        Directory.CreateDirectory(notARepository);
        var result = await git.OpenRepositoryAsync(notARepository);

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryOpenFailure.NotARepository, result.Failure);
        Assert.Contains("不是 Git 仓库", result.FailureMessage);
    }

    [Fact]
    public async Task 端到端_打开提交数不足的仓库时提示无法构成变更范围()
    {
        var (_, git, environment) = CreateSession();
        await environment.GetStatusAsync();

        // 「提交数不足」不是打开失败：仓库照常打开，由 HasEnoughCommits 告诉界面显示警告横幅。
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "single", commitCount: 1);
        var result = await git.OpenRepositoryAsync(repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Repository!.CommitCount);
        Assert.False(result.Repository.HasEnoughCommits);
    }

    [Fact]
    public async Task 端到端_重开最近列表中已被删除的仓库得到目录不存在提示()
    {
        // 会话 1：打开仓库并进入最近列表；随后仓库目录被删除/移动。
        var (settings, git, _) = CreateSession();
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "doomed", commitCount: 2);
        var opened = await git.OpenRepositoryAsync(repositoryPath);
        settings.AddRecentRepository(opened.Repository!.RootPath);
        TestDirectory.DeleteDirectory(repositoryPath);

        // 会话 2（重启）：从最近列表取出仓库根重开——这正是首页「一键重开」的路径。
        var (restartedSettings, restartedGit, _) = CreateSession();
        var recentRoot = restartedSettings.RecentRepositories[0];
        var result = await restartedGit.OpenRepositoryAsync(recentRoot);

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryOpenFailure.DirectoryNotFound, result.Failure);
        Assert.Contains("不存在", result.FailureMessage);
    }

    [Fact]
    public async Task 端到端_重启后恢复设置最近仓库与上次分支()
    {
        var outputPath = Path.Combine(_directory.Path, "更新包输出");

        // 会话 1：改全局设置、打开仓库并记忆（最近列表 + 上次分支字段，后者是 ticket 06 选分支时会写的值）。
        var (settings, git, _) = CreateSession();
        settings.Save(new GlobalSettings { DefaultOutputPath = outputPath });
        var repositoryPath = await TestRepository.CreateAsync(_directory.Path, "remembered", commitCount: 2);
        var opened = await git.OpenRepositoryAsync(repositoryPath);
        var root = opened.Repository!.RootPath;
        settings.AddRecentRepository(root);
        settings.SaveRepositoryState(root, new RepositoryState { LastBranch = opened.Repository.CurrentBranch! });

        // 会话 2（重启）：同一组文件路径构造全新实例，全部状态应从盘上恢复。
        var (restarted, _, _) = CreateSession();

        Assert.Equal(outputPath, restarted.Settings.DefaultOutputPath);
        Assert.Equal(TestPaths.Normalize(root), TestPaths.Normalize(restarted.RecentRepositories[0]));
        Assert.Equal("main", restarted.GetRepositoryState(root).LastBranch);
    }

    public void Dispose() => _directory.Dispose();

    /// <summary>
    /// 模拟一次应用启动：按组合根（App.BuildServices）的顺序构造 Core 服务。
    /// 设置文件落在测试目录下（而非真实 %APPDATA%），构造 SettingsService 即触发首启生成，
    /// git 探测走真实环境。
    /// </summary>
    private (SettingsService Settings, GitService Git, IGitEnvironmentService GitEnvironment) CreateSession()
    {
        var settings = new SettingsService(SettingsFilePath, RepositoryStateFilePath, SilentLogger);
        var environment = new GitEnvironmentService(
            GitExecutableLocator.ForCurrentEnvironment(),
            new GitCliRunner(),
            settings,
            SilentLogger);
        var git = new GitService(environment, new GitCliRunner(), SilentLogger);
        return (settings, git, environment);
    }
}
