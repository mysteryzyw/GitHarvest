using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 文件日志的落地约定：按天滚动、保留最近 30 天，写入日志目录下的 log-*.txt。
/// 应用启动时调用一次，并把返回的日志器赋给 Serilog 的全局 <c>Log.Logger</c>。
/// </summary>
public static class LoggingSetup
{
    /// <summary>日志文件名模板（Serilog 会在 <c>-</c> 与 <c>.txt</c> 之间插入日期）。</summary>
    public const string LogFileNameTemplate = "log-.txt";

    /// <summary>
    /// 日志文件的通配模式：清理时按它枚举（见 <see cref="DataMaintenanceService"/>）。
    /// 与 <see cref="LogFileNameTemplate"/> 是一对——改了模板忘了改这里，清理就会漏掉文件。
    /// </summary>
    public const string LogFileSearchPattern = "log-*.txt";

    /// <summary>
    /// 保留的日志文件数量。Serilog 的保留策略按文件个数计，配合按天滚动即「保留最近 30 天」。
    /// 这是**兜底**：用户可在全局设置里配「数据保留天数」，由
    /// <see cref="DataMaintenanceService.PruneExpired"/> 按天数真正执行清理；
    /// 两者不冲突（个数上限大、天数上限小，天数先生效）。
    /// </summary>
    public const int RetainedFileCountLimit = 30;

    /// <summary>
    /// 建立写入指定日志目录的文件日志器。目录不存在时自动创建。
    /// </summary>
    /// <param name="logDirectory">日志目录，取自 <see cref="IDataLocation.LogDirectory"/>。</param>
    public static Logger ConfigureFileLogging(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDirectory, LogFileNameTemplate),
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit)
            .CreateLogger();
    }
}
