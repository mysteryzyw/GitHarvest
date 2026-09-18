using GitHarvest.Core.Infrastructure;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 应用数据目录与日志目录的位置约定（%APPDATA%\GitHarvest\...）。
/// </summary>
public class AppPathsTests
{
    private const string RoamingRoot = @"C:\Users\tester\AppData\Roaming";

    [Fact]
    public void 数据目录位于漫游应用数据下的产品目录()
    {
        Assert.Equal(
            Path.Combine(RoamingRoot, "GitHarvest"),
            AppPaths.GetDataDirectory(RoamingRoot));
    }

    [Fact]
    public void 日志目录位于数据目录的日志子目录()
    {
        Assert.Equal(
            Path.Combine(RoamingRoot, "GitHarvest", "logs"),
            AppPaths.GetLogDirectory(RoamingRoot));
    }

    [Theory]
    [InlineData(@"C:\Users\tester\AppData\Roaming\")]
    [InlineData(@"C:\Users\tester\AppData\Roaming\\")]
    public void 漫游根目录带多余分隔符时结果不变(string trailingSeparatorRoot)
    {
        Assert.Equal(
            AppPaths.GetDataDirectory(RoamingRoot),
            AppPaths.GetDataDirectory(trailingSeparatorRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 漫游根目录为空时明确失败(string invalidRoot)
    {
        Assert.Throws<ArgumentException>(() => AppPaths.GetDataDirectory(invalidRoot));
        Assert.Throws<ArgumentException>(() => AppPaths.GetLogDirectory(invalidRoot));
    }

    [Fact]
    public void 漫游根目录为null时明确失败()
    {
        Assert.Throws<ArgumentNullException>(() => AppPaths.GetDataDirectory(null!));
        Assert.Throws<ArgumentNullException>(() => AppPaths.GetLogDirectory(null!));
    }

    [Fact]
    public void 无参重载解析出绝对路径并落在产品目录下()
    {
        var dataDirectory = AppPaths.GetDataDirectory();
        var logDirectory = AppPaths.GetLogDirectory();

        Assert.True(Path.IsPathRooted(dataDirectory));
        Assert.True(Path.IsPathRooted(logDirectory));
        Assert.EndsWith(Path.Combine("GitHarvest", "logs"), logDirectory);
        Assert.Equal(Path.Combine(dataDirectory, "logs"), logDirectory);
    }
}
