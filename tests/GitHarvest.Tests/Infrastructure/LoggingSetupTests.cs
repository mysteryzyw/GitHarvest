using GitHarvest.Core.Infrastructure;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 文件日志的外部行为：落到当日文件、内容可读、错误含堆栈、只保留最近 30 天。
/// </summary>
public class LoggingSetupTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void 日志写入当日文件且日志目录不存在时自动创建()
    {
        var logDirectory = Path.Combine(_directory.Path, "logs");

        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            logger.Information("打开仓库 {Repository}", @"D:\Projects\DemoService.Api");
        }

        var file = Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt"));
        Assert.Equal($"log-{DateTime.Now:yyyyMMdd}.txt", Path.GetFileName(file));
        Assert.Contains("打开仓库", File.ReadAllText(file));
    }

    [Fact]
    public void 错误日志记录异常信息与调用堆栈()
    {
        var logDirectory = _directory.Path;

        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            try
            {
                throw new InvalidOperationException("仓库不可用");
            }
            catch (Exception exception)
            {
                logger.Error(exception, "打开仓库失败");
            }
        }

        var content = ReadSingleLogFile(logDirectory);
        Assert.Contains("打开仓库失败", content);
        Assert.Contains(nameof(InvalidOperationException), content);
        Assert.Contains("仓库不可用", content);
        Assert.Contains("at ", content);
    }

    [Fact]
    public void 中文路径与提交信息可正确写入()
    {
        var logDirectory = _directory.Path;

        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            logger.Information(
                "变更文件 {Path} 提交信息 {Message} 作者 {Author}",
                "客户资料/更新说明.md",
                "修复中文文件名导出",
                "张三");
        }

        var content = ReadSingleLogFile(logDirectory);
        Assert.Contains("客户资料/更新说明.md", content);
        Assert.Contains("修复中文文件名导出", content);
        Assert.Contains("张三", content);
    }

    [Fact]
    public void 只保留最近三十天的日志文件()
    {
        var logDirectory = _directory.Path;
        var today = DateTime.Now.Date;

        for (var daysAgo = 1; daysAgo <= 35; daysAgo++)
        {
            File.WriteAllText(
                Path.Combine(logDirectory, $"log-{today.AddDays(-daysAgo):yyyyMMdd}.txt"),
                "历史日志");
        }

        using (var logger = LoggingSetup.ConfigureFileLogging(logDirectory))
        {
            logger.Information("当日日志");
        }

        var remaining = Directory
            .GetFiles(logDirectory, "log-*.txt")
            .Select(Path.GetFileName)
            .Order()
            .ToArray();

        Assert.True(
            remaining.Length <= 30,
            $"期望最多保留 30 个日志文件，实际 {remaining.Length} 个：{string.Join(", ", remaining)}");
        Assert.Contains($"log-{today:yyyyMMdd}.txt", remaining);
    }

    private static string ReadSingleLogFile(string logDirectory)
        => File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "log-*.txt")));
}
