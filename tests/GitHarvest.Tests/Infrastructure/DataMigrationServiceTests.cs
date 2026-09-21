using GitHarvest.Core.Infrastructure;
using GitHarvest.Tests.Support;
using Serilog;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 数据目录迁移（<c>IDataMigrationService</c>）：复制全部数据、排除指针文件、写指针，
/// 以及各种「不该让它迁」的目标（非空、在当前目录里、等于当前目录）。
/// 迁移 = 复制不 = 移动：原目录必须原封不动地留着（用户核对无误后自己删）。
/// </summary>
public sealed class DataMigrationServiceTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    private static readonly ILogger SilentLogger = new LoggerConfiguration().CreateLogger();

    private string RoamingRoot => _directory.Path;

    private string DataDirectory => Path.Combine(_directory.Path, "GitHarvest");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void 迁移复制全部文件与子目录并写指针()
    {
        var location = CreateLocationWithData();

        var target = Path.Combine(_directory.Path, "新位置");
        var result = CreateService(location).MigrateTo(target);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(4, result.CopiedFileCount); // 3 个数据文件 + logs 下的 1 个日志

        // 设置、每仓库状态、导出历史都在；logs 子目录连内容一起搬过去。
        Assert.True(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "repository-state.json")));
        Assert.True(File.Exists(Path.Combine(target, "export-history.jsonl")));
        Assert.Equal("日志内容", File.ReadAllText(Path.Combine(target, "logs", "log-20260920.txt")));

        // 新实例模拟重启：位置已经改到新目录。
        Assert.Equal(target, new DataLocation(RoamingRoot).DataDirectory);
    }

    [Fact]
    public void 迁移保留原目录的一切()
    {
        var location = CreateLocationWithData();
        var target = Path.Combine(_directory.Path, "新位置");

        Assert.True(CreateService(location).MigrateTo(target).IsSuccess);

        // 原目录一个文件都不能少：迁移是复制，不是搬走。
        Assert.True(File.Exists(Path.Combine(DataDirectory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(DataDirectory, "export-history.jsonl")));
        Assert.True(File.Exists(Path.Combine(DataDirectory, "logs", "log-20260920.txt")));
    }

    [Fact]
    public void 迁移不复制指针文件本身()
    {
        var location = CreateLocationWithData();
        // 先迁一次，让默认目录里存在指针文件。
        var first = Path.Combine(_directory.Path, "第一次");
        Assert.True(CreateService(location).MigrateTo(first).IsSuccess);
        Assert.True(File.Exists(Path.Combine(DataDirectory, "location.json")));

        var second = Path.Combine(_directory.Path, "第二次");
        var result = CreateService(new DataLocation(RoamingRoot)).MigrateTo(second);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.False(
            File.Exists(Path.Combine(second, "location.json")),
            "指针文件是「位置」信息，不属于用户数据，不应被复制到新数据目录里");
    }

    [Fact]
    public void 目标目录非空时拒绝且不动任何数据()
    {
        var location = CreateLocationWithData();
        var target = Path.Combine(_directory.Path, "非空目录");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "用户自己的文件.txt"), "别动我");

        var result = CreateService(location).MigrateTo(target);

        Assert.False(result.IsSuccess);
        Assert.Contains("不是空的", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Equal("别动我", File.ReadAllText(Path.Combine(target, "用户自己的文件.txt")));
        Assert.Equal(DataDirectory, location.DataDirectory);
    }

    [Fact]
    public void 目标在当前数据目录之内时拒绝()
    {
        var location = CreateLocationWithData();
        var target = Path.Combine(DataDirectory, "子目录");

        var result = CreateService(location).MigrateTo(target);

        Assert.False(result.IsSuccess);
        Assert.Contains("不能在它里面", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Equal(DataDirectory, location.DataDirectory);
    }

    [Fact]
    public void 目标是当前数据目录的上级时拒绝()
    {
        var location = CreateLocationWithData();

        var result = CreateService(location).MigrateTo(_directory.Path);

        Assert.False(result.IsSuccess);
        Assert.Contains("上级目录", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void 目标是当前数据目录本身时拒绝()
    {
        var location = CreateLocationWithData();

        var result = CreateService(location).MigrateTo(DataDirectory);

        Assert.False(result.IsSuccess);
        Assert.Equal(DataDirectory, location.DataDirectory);
    }

    [Fact]
    public void 目标路径为空时拒绝()
    {
        var result = CreateService(CreateLocationWithData()).MigrateTo("   ");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void 迁移过程中失败会回滚且指针不变()
    {
        var location = CreateLocationWithData();

        // 超长路径：校验能过（目录不存在），到创建/复制那一步必然失败——用来验证回滚路径。
        var tooLong = Path.Combine(_directory.Path, new string('x', 300));

        var result = CreateService(location).MigrateTo(tooLong);

        Assert.False(result.IsSuccess);
        Assert.Equal(DataDirectory, location.DataDirectory);
        Assert.False(Directory.Exists(tooLong), "失败后不应留下半个目标目录");
        Assert.True(File.Exists(Path.Combine(DataDirectory, "settings.json")), "原数据必须完好");
    }

    /// <summary>造一个默认数据目录（含设置、状态、历史与一份日志）。</summary>
    private DataLocation CreateLocationWithData()
    {
        Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(DataDirectory, "repository-state.json"), "{}");
        File.WriteAllText(Path.Combine(DataDirectory, "export-history.jsonl"), "");
        File.WriteAllText(Path.Combine(DataDirectory, "logs", "log-20260920.txt"), "日志内容");

        return new DataLocation(RoamingRoot);
    }

    private static DataMigrationService CreateService(IDataLocation location)
        => new(location, SilentLogger);
}
