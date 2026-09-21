using GitHarvest.Core.History;
using GitHarvest.Core.Infrastructure;
using GitHarvest.Core.Settings;
using GitHarvest.Tests.Support;
using Serilog;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 数据与日志的清理（<c>IDataMaintenanceService</c>）：两档删除的范围、按保留天数清理、
/// 以及「删不掉的文件如实报告」。全部用真实临时目录——删除的范围（删了什么、没删什么）
/// 只有落在真实文件上才验得出来。
/// </summary>
public sealed class DataMaintenanceServiceTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    private static readonly ILogger SilentLogger = new LoggerConfiguration().CreateLogger();

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.FromHours(8));

    private string RoamingRoot => _directory.Path;

    private string DataDirectory => Path.Combine(_directory.Path, "GitHarvest");

    private string LogDirectory => Path.Combine(DataDirectory, "logs");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void 清理历史与日志会删掉它们并让统计归零()
    {
        var (maintenance, history, _) = CreateServices();
        WriteLogFile("log-20260901.txt", "旧日志");
        WriteLogFile("log-20260902.txt", "旧日志");
        history.Append(Entry(Now.AddDays(-1), @"D:\Code\Demo"));
        history.Append(Entry(Now.AddDays(-2), @"D:\Code\Demo"));
        Assert.Equal(2, history.GetStatistics().PackageCount);

        var result = maintenance.ClearHistoryAndLogs();

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(3, result.DeletedFileCount); // 2 个日志 + 1 个历史文件
        Assert.Equal(2, result.RemovedHistoryCount);
        Assert.Empty(result.SkippedFiles);
        Assert.False(File.Exists(Path.Combine(DataDirectory, "export-history.jsonl")));
        Assert.Empty(Directory.GetFiles(LogDirectory));

        // 内存统计同步归零：首页立刻显示 0，而不是等重启。
        Assert.Equal(0, history.GetStatistics().PackageCount);
        Assert.Null(history.GetLastExportedAt(@"D:\Code\Demo"));
    }

    [Fact]
    public void 清理历史与日志保留设置与每仓库状态()
    {
        var (maintenance, _, settings) = CreateServices();

        maintenance.ClearHistoryAndLogs();

        Assert.True(File.Exists(Path.Combine(DataDirectory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(DataDirectory, "repository-state.json")));
        Assert.Equal(@"D:\交付物", settings.Settings.DefaultOutputPath);
    }

    [Fact]
    public void 重置全部数据会删掉设置与每仓库状态并按默认值重新生成()
    {
        var (maintenance, history, settings) = CreateServices();
        WriteLogFile("log-20260901.txt", "旧日志");
        history.Append(Entry(Now.AddDays(-1), @"D:\Code\Demo"));

        var result = maintenance.ResetAllData();

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Empty(Directory.GetFiles(LogDirectory));

        // 设置文件被删掉后由 Reload 按默认值重新生成——用户看到的是「恢复出厂」。
        Assert.True(File.Exists(Path.Combine(DataDirectory, "settings.json")));
        Assert.Equal(string.Empty, settings.Settings.DefaultOutputPath);
        Assert.Equal(GlobalSettings.DefaultDataRetentionDays, settings.Settings.DataRetentionDays);
        Assert.Empty(settings.RecentRepositories);
        Assert.Equal(0, history.GetStatistics().PackageCount);
    }

    [Fact]
    public void 重置全部数据不删数据目录指针文件()
    {
        // 先迁到自定义目录，制造出一个指针文件。
        var custom = Directory.CreateDirectory(Path.Combine(_directory.Path, "自定义数据目录")).FullName;
        var location = new DataLocation(RoamingRoot);
        Assert.True(new DataMigrationService(location, SilentLogger).MigrateTo(custom).IsSuccess);
        Assert.True(File.Exists(Path.Combine(DataDirectory, "location.json")));

        var (maintenance, _, _) = CreateServices(location);
        maintenance.ResetAllData();

        // 位置配置与用户数据分开对待：重置后仍指向自定义目录，不会「悄悄回到默认目录」。
        Assert.True(File.Exists(Path.Combine(DataDirectory, "location.json")));
        Assert.Equal(custom, new DataLocation(RoamingRoot).DataDirectory);
    }

    [Fact]
    public void 保留天数为零时不清理()
    {
        var (maintenance, history, _) = CreateServices();
        WriteLogFile("log-20200101.txt", "很久以前的日志");
        history.Append(Entry(Now.AddDays(-400), @"D:\Code\Demo"));

        var result = maintenance.PruneExpired(retentionDays: 0, Now);

        Assert.Equal(0, result.DeletedFileCount);
        Assert.Equal(0, result.RemovedHistoryCount);
        Assert.True(File.Exists(Path.Combine(LogDirectory, "log-20200101.txt")));
        Assert.Equal(1, history.GetStatistics().PackageCount);
    }

    [Fact]
    public void 按保留天数只清理过期的日志与历史()
    {
        var (maintenance, history, _) = CreateServices();
        WriteLogFile("log-20200101.txt", "过期日志", lastWrite: Now.AddDays(-40));
        WriteLogFile("log-20260920.txt", "没过期", lastWrite: Now.AddDays(-1));

        history.Append(Entry(Now.AddDays(-40), @"D:\Code\旧仓库"));
        history.Append(Entry(Now.AddDays(-40), @"D:\Code\旧仓库"));
        history.Append(Entry(Now.AddDays(-2), @"D:\Code\新仓库"));

        var result = maintenance.PruneExpired(retentionDays: 30, Now);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.Equal(2, result.RemovedHistoryCount);
        Assert.False(File.Exists(Path.Combine(LogDirectory, "log-20200101.txt")));
        Assert.True(File.Exists(Path.Combine(LogDirectory, "log-20260920.txt")));

        // 统计跟着变小（只剩那条新记录），且重启后读回的文件也是同一口径。
        Assert.Equal(1, history.GetStatistics().PackageCount);
        Assert.Equal(1, new HistoryService(Path.Combine(DataDirectory, "export-history.jsonl"), SilentLogger)
            .GetStatistics().PackageCount);
    }

    [Fact]
    public void 被占用的日志文件被跳过并如实报告()
    {
        var (maintenance, _, _) = CreateServices();
        WriteLogFile("log-20260901.txt", "被占用的日志");
        var lockedPath = Path.Combine(LogDirectory, "log-20260901.txt");

        // 独占打开：模拟「日志正被另一个进程写着」，此时删除必然失败。
        using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = maintenance.ClearHistoryAndLogs();

            Assert.True(result.IsSuccess, result.FailureMessage);
            Assert.Contains(lockedPath, result.SkippedFiles);
            Assert.True(File.Exists(lockedPath));
        }

        // 占用解除后再清一次就该成功（不是永久性失败）。
        Assert.True(maintenance.ClearHistoryAndLogs().SkippedFiles.Count == 0);
        Assert.False(File.Exists(lockedPath));
    }

    [Fact]
    public void 日志目录被外部换成同名文件时不抛异常且如实报告()
    {
        var (maintenance, history, _) = CreateServices();
        WriteLogFile("log-20260901.txt", "旧日志");
        history.Append(Entry(Now.AddDays(-1), @"D://Code//Demo"));

        // 用户在资源管理器里把 logs 删掉并建了个同名文件：日志没得清了，但历史照常清。
        Directory.Delete(LogDirectory, recursive: true);
        File.WriteAllText(LogDirectory, "占位");

        var result = maintenance.ClearHistoryAndLogs();

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Contains(LogDirectory, result.SkippedFiles);
        Assert.Equal(0, history.GetStatistics().PackageCount);
    }

    [Fact]
    public void 没有可清理的内容时不报错()
    {
        var (maintenance, _, _) = CreateServices();

        var result = maintenance.PruneExpired(retentionDays: 30, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.DeletedFileCount);
    }

    /// <summary>造一套真实服务：数据目录位置、设置服务与导出历史服务都落在临时目录里。</summary>
    private (DataMaintenanceService Maintenance, HistoryService History, SettingsService Settings) CreateServices(
        IDataLocation? location = null)
    {
        Directory.CreateDirectory(DataDirectory);
        var resolved = location ?? new DataLocation(RoamingRoot);

        var settings = new SettingsService(resolved.SettingsFilePath, resolved.RepositoryStateFilePath, SilentLogger);
        settings.Save(settings.Settings with { DefaultOutputPath = @"D:\交付物" });
        settings.AddRecentRepository(@"D:\Code\Demo");

        var history = new HistoryService(resolved.ExportHistoryFilePath, SilentLogger);

        return (new DataMaintenanceService(resolved, settings, history, SilentLogger), history, settings);
    }

    private void WriteLogFile(string fileName, string content, DateTimeOffset? lastWrite = null)
    {
        Directory.CreateDirectory(LogDirectory);
        var path = Path.Combine(LogDirectory, fileName);
        File.WriteAllText(path, content);

        if (lastWrite is { } moment)
        {
            File.SetLastWriteTimeUtc(path, moment.UtcDateTime);
        }
    }

    private static ExportHistoryEntry Entry(DateTimeOffset exportedAt, string repositoryPath)
        => new(
            exportedAt,
            repositoryPath,
            BranchName: "main",
            BaseHash: "a1b2c3d",
            HeadHash: "e4f5g6h",
            OutputPath: @"D:\交付物\2026-09-20");
}
