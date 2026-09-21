using System.Text;
using System.Text.Json;
using GitHarvest.Core.History;
using GitHarvest.Tests.Support;
using Serilog;

namespace GitHarvest.Tests.History;

/// <summary>
/// 导出历史（<c>IHistoryService</c>）的行为测试：追加、容错与首页统计。
/// 全部通过真实临时文件验证——「跨进程重开能读回」「坏行不拖垮整个文件」这类容错性质
/// 只有落到真实文件上才成立，用内存桩测不出契约。
/// </summary>
public sealed class HistoryServiceTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    private static readonly ILogger SilentLogger = new LoggerConfiguration().CreateLogger();

    private static readonly DateTimeOffset Moment =
        new(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8));

    private string HistoryFilePath => Path.Combine(_directory.Path, "导出历史", "export-history.jsonl");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void 追加后新实例能读回记录()
    {
        var entry = Entry(Moment, @"D:\Code\DemoService", "main");

        CreateService().Append(entry);

        // 新实例模拟应用重启：持久化必须落在磁盘上，而不是只在内存里。
        var reopened = CreateService();
        Assert.Equal(1, reopened.GetStatistics().PackageCount);
        Assert.Equal(Moment, reopened.GetLastExportedAt(@"D:\Code\DemoService"));
    }

    [Fact]
    public void 每次追加写出一行JSON并保留仓库与范围信息()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\DemoService", "main"));
        service.Append(Entry(Moment.AddHours(1), @"D:\Code\OtherService", "dev"));

        var lines = File.ReadAllLines(HistoryFilePath);
        Assert.Equal(2, lines.Length);

        using var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;
        Assert.Equal(@"D:\Code\DemoService", root.GetProperty("repositoryPath").GetString());
        Assert.Equal("main", root.GetProperty("branchName").GetString());
        Assert.Equal("a1b2c3d", root.GetProperty("baseHash").GetString());
        Assert.Equal("e4f5g6h", root.GetProperty("headHash").GetString());
        Assert.Equal(@"D:\交付物\2026-09-20", root.GetProperty("outputPath").GetString());
        Assert.Equal(Moment, root.GetProperty("exportedAt").GetDateTimeOffset());

        // 追加语义：旧记录在前、新记录在后（文件只增不改）。
        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal(@"D:\Code\OtherService", second.RootElement.GetProperty("repositoryPath").GetString());
    }

    [Fact]
    public void 历史更新包数等于记录条数()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\DemoService", "main"));
        service.Append(Entry(Moment.AddHours(1), @"D:\Code\DemoService", "main"));
        service.Append(Entry(Moment.AddHours(2), @"D:\Code\OtherService", "dev"));

        Assert.Equal(3, service.GetStatistics().PackageCount);
    }

    [Fact]
    public void 最常用分支取出现次数最多者()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\A", "release/2.4"));
        service.Append(Entry(Moment.AddHours(1), @"D:\Code\B", "main"));
        service.Append(Entry(Moment.AddHours(2), @"D:\Code\C", "main"));

        Assert.Equal("main", service.GetStatistics().MostUsedBranch);
    }

    [Fact]
    public void 最常用分支并列时取最近一次导出所在的分支()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\A", "main"));
        service.Append(Entry(Moment.AddDays(1), @"D:\Code\B", "dev"));

        Assert.Equal("dev", service.GetStatistics().MostUsedBranch);
    }

    [Fact]
    public void 分支为空的记录不参与最常用分支统计()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\A", "  "));
        service.Append(Entry(Moment.AddHours(1), @"D:\Code\B", "dev"));

        Assert.Equal("dev", service.GetStatistics().MostUsedBranch);
    }

    [Fact]
    public void 没有任何记录时统计为零且没有最常用分支()
    {
        var statistics = CreateService().GetStatistics();

        Assert.Equal(0, statistics.PackageCount);
        Assert.Null(statistics.MostUsedBranch);
    }

    [Fact]
    public void 历史文件不存在时视为空历史且首次追加会创建文件()
    {
        var service = CreateService();

        Assert.False(File.Exists(HistoryFilePath));
        Assert.Null(service.GetLastExportedAt(@"D:\Code\DemoService"));

        service.Append(Entry(Moment, @"D:\Code\DemoService", "main"));

        Assert.True(File.Exists(HistoryFilePath));
    }

    [Fact]
    public void 损坏的行只被跳过不影响其余记录()
    {
        WriteHistoryFile(
            "{ 这不是 JSON",
            Line(Moment, @"D:\Code\A", "main"),
            "{}",
            Line(Moment.AddHours(1), @"D:\Code\B", "main"));

        // 两个坏行（语法错误、缺仓库路径）被丢弃，两条真记录仍在。
        Assert.Equal(2, CreateService().GetStatistics().PackageCount);
    }

    [Fact]
    public void 整个文件损坏时视为空历史而不抛出()
    {
        WriteHistoryFile("\u0000\u0000乱码", "也是乱码");

        var service = CreateService();

        Assert.Equal(0, service.GetStatistics().PackageCount);
        Assert.Null(service.GetStatistics().MostUsedBranch);
    }

    [Fact]
    public void 写入失败时不影响调用方且本次会话内统计仍可见()
    {
        // 把「导出历史」做成一个同名文件，追加时建目录必然失败（磁盘满/无权限的等价场景）。
        var blockedRoot = Path.Combine(_directory.Path, "导出历史");
        File.WriteAllText(blockedRoot, "占位");

        var service = new HistoryService(
            Path.Combine(blockedRoot, "export-history.jsonl"),
            SilentLogger);

        service.Append(Entry(Moment, @"D:\Code\DemoService", "main"));

        // 导出主流程绝不能因为写不进历史而中断；内存快照保留本次记录。
        Assert.Equal(1, service.GetStatistics().PackageCount);
    }

    [Fact]
    public void 最近导出时间按仓库匹配且忽略大小写与分隔符差异()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\DemoService\", "main"));

        Assert.Equal(Moment, service.GetLastExportedAt(@"d:/code/demoservice"));
        Assert.Null(service.GetLastExportedAt(@"D:\Code\OtherService"));
    }

    [Fact]
    public void 最近导出时间取该仓库最新的一条()
    {
        var service = CreateService();
        service.Append(Entry(Moment, @"D:\Code\DemoService", "main"));
        service.Append(Entry(Moment.AddDays(2), @"D:\Code\DemoService", "release/2.4"));
        service.Append(Entry(Moment.AddDays(9), @"D:\Code\OtherService", "main"));

        Assert.Equal(Moment.AddDays(2), service.GetLastExportedAt(@"D:\Code\DemoService"));
    }

    [Fact]
    public void 仓库路径为空时明确失败()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.GetLastExportedAt("  "));
    }

    [Fact]
    public void 追加空记录或缺少仓库路径时明确失败()
    {
        var service = CreateService();

        Assert.Throws<ArgumentNullException>(() => service.Append(null!));
        Assert.Throws<ArgumentException>(
            () => service.Append(Entry(Moment, "  ", "main")));
    }

    private HistoryService CreateService() => new(HistoryFilePath, SilentLogger);

    private static ExportHistoryEntry Entry(DateTimeOffset exportedAt, string repositoryPath, string branchName)
        => new(
            exportedAt,
            repositoryPath,
            branchName,
            BaseHash: "a1b2c3d",
            HeadHash: "e4f5g6h",
            OutputPath: @"D:\交付物\2026-09-20");

    /// <summary>把若干原始行写进历史文件（模拟手改或历史遗留的坏数据）。</summary>
    private void WriteHistoryFile(params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath)!);
        File.WriteAllLines(HistoryFilePath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Line(DateTimeOffset exportedAt, string repositoryPath, string branchName)
        => JsonSerializer.Serialize(Entry(exportedAt, repositoryPath, branchName));
}
