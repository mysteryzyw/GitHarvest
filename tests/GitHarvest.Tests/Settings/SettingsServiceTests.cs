using System.Text.Json;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Settings;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Settings;

/// <summary>
/// 全局设置的 JSON 持久化（settings.json）：
/// 首次启动自动生成含四项默认值的文件；手写文件宽容解析（注释/尾逗号/大小写不敏感）；
/// 损坏或缺失时回退默认值并记 Warning，不崩溃；保存后的值在新实例（模拟重启）中保持。
/// 只通过 <see cref="ISettingsService"/> 与磁盘上的 settings.json 观察外部行为。
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    private string SettingsFilePath => Path.Combine(_directory.Path, "settings.json");

    private string RepositoryStateFilePath => Path.Combine(_directory.Path, "repository-state.json");

    [Fact]
    public void 首次启动自动生成含四项默认值的settings_json()
    {
        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            CreateService(logger);
        }

        // 生成的文件本身就是可手改的持久化契约：四个顶层字段必须齐全且取默认值。
        // 字段名（含 gitExecutablePath）一经发布不得更改，否则破坏既有用户的手改文件。
        Assert.True(File.Exists(SettingsFilePath));
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsFilePath));
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("defaultOutputPath", out var defaultOutputPath));
        Assert.True(root.TryGetProperty("defaultTemplatePath", out var defaultTemplatePath));
        Assert.True(root.TryGetProperty("defaultTheme", out var defaultTheme));
        Assert.True(root.TryGetProperty("gitExecutablePath", out var gitExecutablePath));
        Assert.Equal(string.Empty, defaultOutputPath.GetString());
        Assert.Equal(string.Empty, defaultTemplatePath.GetString());
        Assert.Equal("Light", defaultTheme.GetString());
        Assert.Equal(string.Empty, gitExecutablePath.GetString());

        // 文件缺失按验收标准记 Warning（首次启动生成前的一次性提示，不影响启动）。
        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("WRN", content);
        Assert.Contains(SettingsFilePath, content);
    }

    [Fact]
    public void 宽容读取手写的设置文件()
    {
        File.WriteAllText(
            SettingsFilePath,
            """
            {
              // 手工加的注释
              "DefaultOutputPath": "D:/交付物",
              "defaultTemplatePath": "D:/模板/更新说明模板.md",
              "defaultTheme": "followSystem",
              "gitExecutablePath": "D:/Tools/Git/cmd/git.exe",
            }
            """);

        var service = CreateService(SilentLogger);

        Assert.Equal(
            new GlobalSettings
            {
                DefaultOutputPath = "D:/交付物",
                DefaultTemplatePath = "D:/模板/更新说明模板.md",
                DefaultTheme = AppThemeOption.FollowSystem,
                GitExecutablePath = "D:/Tools/Git/cmd/git.exe",
            },
            service.Settings);
    }

    [Fact]
    public void 保存后的设置在下次启动时保持()
    {
        var first = CreateService(SilentLogger);
        first.Save(new GlobalSettings
        {
            DefaultOutputPath = @"E:\更新包",
            DefaultTemplatePath = @"D:\模板\更新说明模板.md",
            DefaultTheme = AppThemeOption.Dark,
            GitExecutablePath = @"D:\Tools\Git\cmd\git.exe",
        });

        // 新实例 = 模拟进程重启后重新加载
        var second = CreateService(SilentLogger);

        Assert.Equal(
            new GlobalSettings
            {
                DefaultOutputPath = @"E:\更新包",
                DefaultTemplatePath = @"D:\模板\更新说明模板.md",
                DefaultTheme = AppThemeOption.Dark,
                GitExecutablePath = @"D:\Tools\Git\cmd\git.exe",
            },
            second.Settings);
        Assert.Equal(@"D:\Tools\Git\cmd\git.exe", ((IGitExecutablePathProvider)second).GitExecutablePath);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "gitExecutablePath": null }""")]
    [InlineData("""{ "gitExecutablePath": "" }""")]
    [InlineData("""{ "gitExecutablePath": "   " }""")]
    public void git路径字段缺失或空白时视为未配置(string json)
    {
        File.WriteAllText(SettingsFilePath, json);

        var service = CreateService(SilentLogger);

        Assert.Null(((IGitExecutablePathProvider)service).GitExecutablePath);
    }

    [Fact]
    public void 设置文件损坏时回退默认值并记Warning且不覆写原文件()
    {
        const string corrupted = "{ 这不是 JSON";
        File.WriteAllText(SettingsFilePath, corrupted);
        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            var service = CreateService(logger);

            Assert.Equal(new GlobalSettings(), service.Settings);
        }

        // 原文件保留不覆写：用户手改坏了语法时，修复原文件比被默认值悄悄覆盖更友好。
        Assert.Equal(corrupted, File.ReadAllText(SettingsFilePath));

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("WRN", content);
        Assert.Contains("全局设置", content);
    }

    [Fact]
    public void 保存设置时记Info日志()
    {
        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            CreateService(logger).Save(new GlobalSettings { DefaultOutputPath = @"E:\更新包" });
        }

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("INF", content);
        Assert.Contains("全局设置", content);
    }

    [Fact]
    public void 每仓库状态按仓库标识独立存取并持久化()
    {
        var first = CreateService(SilentLogger);
        first.SaveRepositoryState(
            @"D:\Code\Foo",
            new RepositoryState { LastOutputPath = @"E:\交付\Foo", LastBranch = "main" });
        first.SaveRepositoryState(@"D:\Code\Bar", new RepositoryState { LastBranch = "dev" });

        // 新实例 = 模拟进程重启后重新加载
        var second = CreateService(SilentLogger);

        Assert.Equal(
            new RepositoryState { LastOutputPath = @"E:\交付\Foo", LastBranch = "main" },
            second.GetRepositoryState(@"D:\Code\Foo"));
        Assert.Equal(
            new RepositoryState { LastBranch = "dev" },
            second.GetRepositoryState(@"D:\Code\Bar"));
    }

    [Theory]
    [InlineData(@"d:\code\foo")]
    [InlineData(@"D:\CODE\FOO")]
    [InlineData("D:/Code/Foo")]
    [InlineData(@"D:\Code\Foo\")]
    public void 仓库路径的大小写分隔符与尾斜杠差异视为同一仓库(string variant)
    {
        var service = CreateService(SilentLogger);
        service.SaveRepositoryState(@"D:\Code\Foo", new RepositoryState { LastBranch = "main" });

        Assert.Equal("main", service.GetRepositoryState(variant).LastBranch);
    }

    [Fact]
    public void 从未记录过的仓库返回默认状态()
    {
        var service = CreateService(SilentLogger);

        Assert.Equal(new RepositoryState(), service.GetRepositoryState(@"D:\Nowhere\Repo"));
    }

    [Fact]
    public void 最近仓库列表追加去重置顶并限制在十条以内()
    {
        var service = CreateService(SilentLogger);
        for (var index = 1; index <= 12; index++)
        {
            service.AddRecentRepository(@$"D:\Repo{index}");
        }

        // 旧条目再次打开：去重并置顶，而不是产生第二份
        service.AddRecentRepository(@"D:\Repo3");

        Assert.Equal(10, service.RecentRepositories.Count);
        Assert.Equal(@"D:\Repo3", service.RecentRepositories[0]);
        Assert.Equal(@"D:\Repo12", service.RecentRepositories[1]);
        Assert.Equal(@"D:\Repo4", service.RecentRepositories[^1]);
        Assert.DoesNotContain(@"D:\Repo1", service.RecentRepositories);
        Assert.DoesNotContain(@"D:\Repo2", service.RecentRepositories);

        // 列表随每次追加立即持久化，新实例可读
        var second = CreateService(SilentLogger);
        Assert.Equal(10, second.RecentRepositories.Count);
        Assert.Equal(@"D:\Repo3", second.RecentRepositories[0]);
    }

    [Fact]
    public void 最近列表中路径写法不同的同一仓库只保留一条()
    {
        var service = CreateService(SilentLogger);
        service.AddRecentRepository(@"D:\Code\Foo");
        service.AddRecentRepository("d:/code/foo");

        // 去重后只剩一条；保留哪一种写法不构成契约（以最后一次加入的规范形式为准），
        // 这里只断言它仍是同一个仓库。
        var entry = Assert.Single(service.RecentRepositories);
        Assert.Equal(@"D:\Code\Foo", entry, ignoreCase: true);
    }

    [Fact]
    public void 仓库状态文件损坏时回退空数据并记Warning且不覆写原文件()
    {
        const string corrupted = "not json at all";
        File.WriteAllText(RepositoryStateFilePath, corrupted);
        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            var service = CreateService(logger);

            Assert.Empty(service.RecentRepositories);
            Assert.Equal(new RepositoryState(), service.GetRepositoryState(@"D:\Code\Foo"));
        }

        // 与 settings.json 相同的策略：损坏的原文件保留不覆写，留给用户修复。
        Assert.Equal(corrupted, File.ReadAllText(RepositoryStateFilePath));

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("WRN", content);
    }

    [Fact]
    public void 仓库状态写入失败时降级为Warning不抛出且内存仍生效()
    {
        var service = CreateService(SilentLogger);
        service.SaveRepositoryState(@"D:\Code\Foo", new RepositoryState { LastBranch = "main" });

        var logDirectory = Path.Combine(_directory.Path, "logs");
        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            var failingService = CreateService(logger);

            // 独占占用状态文件，模拟磁盘满/文件被占用等写入失败场景：
            // 状态是辅助记忆，写失败应降级为 Warning，而不是让选分支这类常规操作炸掉 UI 流程。
            using (File.Open(RepositoryStateFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                failingService.SaveRepositoryState(@"D:\Code\Foo", new RepositoryState { LastBranch = "dev" });
            }

            // 内存快照保持已更新：本次会话内继续生效，重启后回落到磁盘上的旧值。
            Assert.Equal("dev", failingService.GetRepositoryState(@"D:\Code\Foo").LastBranch);
        }

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
        Assert.Contains("WRN", content);
    }

    [Fact]
    public void 全局设置保存失败时向上抛出且内存快照不变()
    {
        var service = CreateService(SilentLogger);

        // 独占占用设置文件：保存是用户显式动作，失败必须抛给调用方反馈（设置页显示错误）。
        // Windows 对被占用目标的 Move 抛 UnauthorizedAccessException，磁盘满等场景抛 IOException——同属「写失败」。
        using (File.Open(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exception = Assert.ThrowsAny<Exception>(
                () => service.Save(new GlobalSettings { DefaultOutputPath = @"E:\更新包" }));
            Assert.True(
                exception is IOException or UnauthorizedAccessException,
                $"意外的异常类型：{exception.GetType()}");
        }

        // 落盘失败不得污染内存快照：内存与磁盘保持一致，用户重试即可。
        Assert.Equal(new GlobalSettings(), service.Settings);
    }

    public void Dispose() => _directory.Dispose();

    private ISettingsService CreateService(Serilog.ILogger logger)
        => new SettingsService(SettingsFilePath, RepositoryStateFilePath, logger);
}
