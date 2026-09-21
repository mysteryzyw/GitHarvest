using System.Text;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 数据目录位置的解析（<c>IDataLocation</c>）：默认位置、指针文件指向自定义目录、
/// 以及指针文件各种坏法下的回退。每一条都对应一种真实场景——用户手改坏了指针文件、
/// 把数据目录挪走/删掉、或者根本没配置过。
/// </summary>
public sealed class DataLocationTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    private string RoamingRoot => _directory.Path;

    private string DefaultDataDirectory => Path.Combine(_directory.Path, "GitHarvest");

    private string LocationFilePath => Path.Combine(DefaultDataDirectory, "location.json");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void 没有指针文件时使用默认数据目录且不报警()
    {
        var location = new DataLocation(RoamingRoot);

        Assert.Equal(DefaultDataDirectory, location.DataDirectory);
        Assert.False(location.IsCustom);
        Assert.Null(location.Notice);
    }

    [Fact]
    public void 指针文件指向自定义目录时使用它且所有派生路径都在其下()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_directory.Path, "交付数据")).FullName;
        WriteLocationFile(custom);

        var location = new DataLocation(RoamingRoot);

        Assert.Equal(custom, location.DataDirectory);
        Assert.True(location.IsCustom);
        Assert.Null(location.Notice);
        Assert.Equal(Path.Combine(custom, "logs"), location.LogDirectory);
        Assert.Equal(Path.Combine(custom, "settings.json"), location.SettingsFilePath);
        Assert.Equal(Path.Combine(custom, "repository-state.json"), location.RepositoryStateFilePath);
        Assert.Equal(Path.Combine(custom, "export-history.jsonl"), location.ExportHistoryFilePath);
    }

    [Fact]
    public void 指针文件路径带尾分隔符时归一化()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_directory.Path, "交付数据")).FullName;
        WriteLocationFile(custom + Path.DirectorySeparatorChar);

        Assert.Equal(custom, new DataLocation(RoamingRoot).DataDirectory);
    }

    [Fact]
    public void 指针文件损坏时回退默认目录并给出提示且保留原文件()
    {
        Directory.CreateDirectory(DefaultDataDirectory);
        File.WriteAllText(LocationFilePath, "{ 这不是 JSON");

        var location = new DataLocation(RoamingRoot);

        Assert.Equal(DefaultDataDirectory, location.DataDirectory);
        Assert.NotNull(location.Notice);
        Assert.True(File.Exists(LocationFilePath), "损坏的指针文件应保留，留给用户自己修");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"dataDirectory\":\"   \"}")]
    [InlineData("{\"dataDirectory\":\"相对路径\\\\data\"}")]
    public void 指针里没有可用的绝对路径时回退默认目录并给出提示(string content)
    {
        Directory.CreateDirectory(DefaultDataDirectory);
        File.WriteAllText(LocationFilePath, content);

        var location = new DataLocation(RoamingRoot);

        Assert.Equal(DefaultDataDirectory, location.DataDirectory);
        Assert.NotNull(location.Notice);
    }

    [Fact]
    public void 指针指向的目录不存在时回退默认目录并给出提示()
    {
        var missing = Path.Combine(_directory.Path, "已被删除的数据目录");
        WriteLocationFile(missing);

        var location = new DataLocation(RoamingRoot);

        Assert.Equal(DefaultDataDirectory, location.DataDirectory);
        Assert.NotNull(location.Notice);
    }

    [Fact]
    public void 写指针后新实例解析到新目录()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_directory.Path, "新数据目录")).FullName;

        new DataLocation(RoamingRoot).SetDataDirectory(custom);

        // 新实例模拟重启：位置必须落在磁盘上，而不是只改了内存。
        var reopened = new DataLocation(RoamingRoot);
        Assert.Equal(custom, reopened.DataDirectory);
        Assert.True(reopened.IsCustom);
        Assert.Null(reopened.Notice);
    }

    [Fact]
    public void 写指针时把路径归一化为完整路径()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_directory.Path, "新数据目录")).FullName;

        new DataLocation(RoamingRoot).SetDataDirectory(Path.Combine(custom, "."));

        Assert.Contains(custom.Replace("\\", "\\\\"), File.ReadAllText(LocationFilePath), StringComparison.Ordinal);
    }

    /// <summary>写一个指向指定目录的指针文件（路径按 JSON 转义）。</summary>
    private void WriteLocationFile(string dataDirectory)
    {
        Directory.CreateDirectory(DefaultDataDirectory);
        File.WriteAllText(
            LocationFilePath,
            $"{{\"dataDirectory\":\"{dataDirectory.Replace("\\", "\\\\")}\"}}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
