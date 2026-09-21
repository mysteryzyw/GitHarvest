using GitHarvest.Core.History;
using GitHarvest.Core.Settings;
using Serilog;

namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 一次数据清理的结论。个别文件删不掉（被别的进程占着）不算失败——
/// 如实列在 <see cref="SkippedFiles"/> 里，让用户知道还剩什么、为什么。
/// </summary>
public sealed record DataMaintenanceResult
{
    private DataMaintenanceResult(
        int deletedFileCount,
        long deletedBytes,
        IReadOnlyList<string> skippedFiles,
        int removedHistoryCount,
        string? failureMessage)
    {
        DeletedFileCount = deletedFileCount;
        DeletedBytes = deletedBytes;
        SkippedFiles = skippedFiles;
        RemovedHistoryCount = removedHistoryCount;
        FailureMessage = failureMessage;
    }

    /// <summary>删掉的文件数（含日志文件与数据文件）。</summary>
    public int DeletedFileCount { get; }

    /// <summary>删掉的总字节数。</summary>
    public long DeletedBytes { get; }

    /// <summary>因被占用等原因跳过的文件（完整路径）；没有则为空列表。</summary>
    public IReadOnlyList<string> SkippedFiles { get; }

    /// <summary>随之清理掉的导出历史条数（首页「历史更新包数」会同步变小）。</summary>
    public int RemovedHistoryCount { get; }

    /// <summary>系统性失败的原因；没有失败时为 <see langword="null"/>（个别文件跳过不算失败）。</summary>
    public string? FailureMessage { get; }

    /// <summary>是否没有发生系统性失败。</summary>
    public bool IsSuccess => FailureMessage is null;

    /// <summary>什么都没做（保留天数为 0，或没有可清理的内容）。</summary>
    public static DataMaintenanceResult Nothing()
        => new(0, 0, [], 0, failureMessage: null);

    /// <summary>构造一次清理的结论。</summary>
    public static DataMaintenanceResult Done(
        int deletedFileCount,
        long deletedBytes,
        IReadOnlyList<string> skippedFiles,
        int removedHistoryCount = 0)
        => new(deletedFileCount, deletedBytes, skippedFiles, removedHistoryCount, failureMessage: null);

    /// <summary>构造系统性失败（连目录都进不去这类）。</summary>
    public static DataMaintenanceResult Failed(string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(0, 0, [], 0, failureMessage);
    }
}

/// <summary>
/// 数据与日志的清理（设置页「清理导出历史与日志」「重置全部数据」与启动时的定期清理）。
/// 三种动作的**范围**是这一层的领域规则：
/// - 清理导出历史与日志：删导出历史文件与日志文件，保留设置与最近仓库；
/// - 重置全部数据：再加上 settings.json 与 repository-state.json（等于恢复出厂），
///   但**保留数据目录的指针文件**——否则重启后会回到默认目录，而那里可能还留着迁移前的旧副本；
/// - 定期清理：按保留天数删日志文件与导出历史，0 表示不自动清理。
/// 删除会破坏「磁盘是真相」的一致性，因此内存快照要在删完后重载（设置服务与导出历史服务各有一个 Reload）。
/// </summary>
public interface IDataMaintenanceService
{
    /// <summary>清理导出历史与日志（保留设置与最近仓库），并让内存统计同步归零。</summary>
    DataMaintenanceResult ClearHistoryAndLogs();

    /// <summary>重置全部用户数据（设置、每仓库状态、导出历史、日志），等于恢复出厂。</summary>
    DataMaintenanceResult ResetAllData();

    /// <summary>
    /// 按保留天数清理日志文件与导出历史：早于 <paramref name="now"/> − 保留天数的内容被删。
    /// 保留天数 ≤ 0 表示不自动清理（直接返回「什么都没做」）。
    /// </summary>
    /// <param name="retentionDays">保留天数（来自全局设置）。</param>
    /// <param name="now">当前时刻；由调用方传入，Core 不读系统时钟（与导出编排同一约定，便于测试）。</param>
    DataMaintenanceResult PruneExpired(int retentionDays, DateTimeOffset now);
}

/// <summary>
/// <see cref="IDataMaintenanceService"/> 的实现：全部是文件系统操作，删不掉的逐个跳过并记账。
/// </summary>
public sealed class DataMaintenanceService : IDataMaintenanceService
{
    private readonly IDataLocation _location;
    private readonly ISettingsService _settings;
    private readonly IHistoryService _history;
    private readonly ILogger _logger;

    public DataMaintenanceService(
        IDataLocation location,
        ISettingsService settings,
        IHistoryService history,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(logger);

        _location = location;
        _settings = settings;
        _history = history;
        _logger = logger;
    }

    /// <inheritdoc />
    public DataMaintenanceResult ClearHistoryAndLogs()
        => RunGuarded("清理导出历史与日志", ClearHistoryAndLogsCore);

    /// <inheritdoc />
    public DataMaintenanceResult ResetAllData()
        => RunGuarded("重置全部数据", ResetAllDataCore);

    /// <inheritdoc />
    public DataMaintenanceResult PruneExpired(int retentionDays, DateTimeOffset now)
    {
        if (retentionDays <= 0)
        {
            // 0（或负数）＝不自动清理：用户明确要求长期保留，什么都不做。
            return DataMaintenanceResult.Nothing();
        }

        return RunGuarded($"按保留 {retentionDays} 天清理", () => PruneExpiredCore(retentionDays, now));
    }

    /// <summary>
    /// 统一兜底：删除过程会遍历目录，而目录可能在遍历中途被外部改动
    /// （用户在资源管理器里删了 logs、别的实例清了同一个数据目录），
    /// 那类异常（<see cref="IOException"/> 家族）必须收敛成失败结论——
    /// 让异常逃到界面线程会把设置页整页炸掉，而这里只是「这次没清成」。
    /// 单个文件删不掉的正常情形由内部的跳过逻辑处理，不走这条兜底。
    /// </summary>
    private DataMaintenanceResult RunGuarded(string action, Func<DataMaintenanceResult> work)
    {
        try
        {
            return work();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException or PathTooLongException)
        {
            _logger.Warning(exception, "{Action}过程中出错，已中止。", action);
            return DataMaintenanceResult.Failed($"{action}失败：{exception.Message}");
        }
    }

    private DataMaintenanceResult ClearHistoryAndLogsCore()
    {
        var skipped = new List<string>();

        // 先记下删之前的记录数：删完再问就是 0 了，而「清掉了多少条历史」正是用户想知道的信息。
        var historyCount = _history.GetStatistics().PackageCount;

        var historyBytes = DeleteFile(_location.ExportHistoryFilePath, skipped);
        var historyDeleted = historyBytes is not null ? 1 : 0;

        var (logFiles, logBytes) = DeleteLogFiles(skipped);

        // 删完才重载：内存里的记录数与磁盘对齐，首页的「历史更新包数」立刻变对。
        _history.Reload();

        var result = DataMaintenanceResult.Done(
            historyDeleted + logFiles,
            (historyBytes ?? 0) + logBytes,
            skipped,
            historyCount);

        LogResult("清理导出历史与日志", result);
        return result;
    }

    private DataMaintenanceResult ResetAllDataCore()
    {
        var skipped = new List<string>();
        var deleted = 0;
        var bytes = 0L;

        var historyCount = _history.GetStatistics().PackageCount;

        // 顺序无关，但把数据文件放在日志之前：日志删除最容易遇到占用，先做能成功的。
        foreach (var file in new[]
                 {
                     _location.SettingsFilePath,
                     _location.RepositoryStateFilePath,
                     _location.ExportHistoryFilePath,
                 })
        {
            if (DeleteFile(file, skipped) is { } size)
            {
                deleted++;
                bytes += size;
            }
        }

        var (logFiles, logBytes) = DeleteLogFiles(skipped);
        deleted += logFiles;
        bytes += logBytes;

        // 注意：**不动** location.json（数据目录位置本身不属于「用户数据」）。

        // 设置文件已被删掉，Reload 会按默认值重新生成它；最近仓库与导出历史同样清空。
        _settings.Reload();
        _history.Reload();

        var result = DataMaintenanceResult.Done(deleted, bytes, skipped, historyCount);
        LogResult("重置全部数据", result);
        return result;
    }

    private DataMaintenanceResult PruneExpiredCore(int retentionDays, DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromDays(retentionDays);
        var skipped = new List<string>();

        var (logFiles, logBytes) = DeleteLogFiles(skipped, olderThan: cutoff);

        var historyBytesBefore = FileSize(_location.ExportHistoryFilePath);
        var removedRecords = _history.PruneBefore(cutoff);
        var historyBytesAfter = FileSize(_location.ExportHistoryFilePath);
        var historyBytes = Math.Max(0, (historyBytesBefore ?? 0) - (historyBytesAfter ?? 0));

        var result = DataMaintenanceResult.Done(logFiles, logBytes + historyBytes, skipped, removedRecords);

        if (result.DeletedFileCount > 0 || result.RemovedHistoryCount > 0)
        {
            LogResult($"按保留 {retentionDays} 天清理", result);
        }

        return result;
    }

    /// <summary>删除日志目录下的日志文件（可选只删早于某时刻的），返回（文件数，字节数）。</summary>
    private (int FileCount, long Bytes) DeleteLogFiles(List<string> skipped, DateTimeOffset? olderThan = null)
    {
        var directory = _location.LogDirectory;

        if (!Directory.Exists(directory))
        {
            // 目录不存在是常态（从没写过日志）；但「路径存在却不是目录」（被换成同名文件）
            // 属于外部改动，要如实报告，否则用户会以为日志已经清干净了。
            if (File.Exists(directory))
            {
                skipped.Add(directory);
            }

            return (0, 0);
        }

        var fileCount = 0;
        var bytes = 0L;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, LoggingSetup.LogFileSearchPattern))
            {
                if (olderThan is { } cutoff && File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime)
                {
                    continue;
                }

                if (DeleteFile(file, skipped) is { } size)
                {
                    fileCount++;
                    bytes += size;
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 遍历中途目录被外部删掉/换掉：能删的已经删了，剩下的如实报告，不让异常往外跑。
            _logger.Warning(exception, "日志目录无法完整遍历，本次只清理了已读到的文件：{LogDirectory}", directory);
            skipped.Add(directory);
        }

        return (fileCount, bytes);
    }

    /// <summary>
    /// 删一个文件并返回它的大小；不存在返回 <see langword="null"/>（不算删过），
    /// 被占用或无权删除则记进 <paramref name="skipped"/> 并返回 <see langword="null"/>。
    /// 删除日志当下的文件必然遇到占用（Serilog 还开着），因此「跳过」是常态而不是异常。
    /// </summary>
    private long? DeleteFile(string path, List<string> skipped)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var size = new FileInfo(path).Length;
            File.Delete(path);
            return size;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Warning(exception, "文件删不掉，已跳过：{FilePath}", path);
            skipped.Add(path);
            return null;
        }
    }

    private static long? FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    private void LogResult(string action, DataMaintenanceResult result)
    {
        if (!result.IsSuccess)
        {
            _logger.Warning("{Action}失败：{FailureMessage}", action, result.FailureMessage);
            return;
        }

        _logger.Information(
            "{Action}完成：删除 {FileCount} 个文件（{Bytes} 字节）、{HistoryCount} 条导出历史；跳过 {SkippedCount} 个被占用的文件",
            action,
            result.DeletedFileCount,
            result.DeletedBytes,
            result.RemovedHistoryCount,
            result.SkippedFiles.Count);
    }
}
