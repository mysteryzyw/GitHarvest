using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Settings;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Settings;

/// <summary>
/// 全局设置里 git.exe 手动路径的只读读取（ticket 03 的 ISettingsService 落地前的最小实现）：
/// 文件缺失、字段缺失或空白都视为「未配置」，文件损坏时记 Warning 后同样按未配置处理，绝不抛出。
/// </summary>
public class SettingsJsonGitPathProviderTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    [Fact]
    public void 设置文件不存在时视为未配置()
    {
        using var logger = LoggingSetup.ConfigureFileLogging(_directory.Path);

        var provider = CreateProvider(logger);

        Assert.Null(provider.GitExecutablePath);
    }

    [Fact]
    public void 读取设置中手动指定的路径()
    {
        using var logger = LoggingSetup.ConfigureFileLogging(_directory.Path);
        WriteSettings(
            """
            {
              "defaultOutputPath": "D:/交付物",
              "gitExecutablePath": "D:/Tools/Git/cmd/git.exe"
            }
            """);

        var provider = CreateProvider(logger);

        Assert.Equal("D:/Tools/Git/cmd/git.exe", provider.GitExecutablePath);
    }

    [Fact]
    public void 字段名大小写不敏感()
    {
        using var logger = LoggingSetup.ConfigureFileLogging(_directory.Path);
        WriteSettings("""{ "GitExecutablePath": "D:/Tools/Git/cmd/git.exe" }""");

        var provider = CreateProvider(logger);

        Assert.Equal("D:/Tools/Git/cmd/git.exe", provider.GitExecutablePath);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "gitExecutablePath": null }""")]
    [InlineData("""{ "gitExecutablePath": "" }""")]
    [InlineData("""{ "gitExecutablePath": "   " }""")]
    public void 字段缺失或空白视为未配置(string json)
    {
        using var logger = LoggingSetup.ConfigureFileLogging(_directory.Path);
        WriteSettings(json);

        var provider = CreateProvider(logger);

        Assert.Null(provider.GitExecutablePath);
    }

    [Fact]
    public void 允许注释与尾逗号的宽松JSON()
    {
        using var logger = LoggingSetup.ConfigureFileLogging(_directory.Path);
        WriteSettings(
            """
            {
              // 手动指定的 git.exe 路径
              "gitExecutablePath": "D:/Tools/Git/cmd/git.exe",
            }
            """);

        var provider = CreateProvider(logger);

        Assert.Equal("D:/Tools/Git/cmd/git.exe", provider.GitExecutablePath);
    }

    [Fact]
    public void 文件损坏时视为未配置并记Warning()
    {
        using (var logger = LoggingSetup.ConfigureFileLogging(_directory.Path))
        {
            WriteSettings("{ 这不是 JSON");

            var provider = CreateProvider(logger);

            Assert.Null(provider.GitExecutablePath);
        }

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory.Path, "log-*.txt")));
        Assert.Contains("全局设置", content);
        Assert.Contains("WRN", content);
    }

    public void Dispose() => _directory.Dispose();

    private SettingsJsonGitPathProvider CreateProvider(Serilog.ILogger logger)
        => new(Path.Combine(_directory.Path, "settings.json"), logger);

    private void WriteSettings(string json)
        => File.WriteAllText(Path.Combine(_directory.Path, "settings.json"), json);
}
