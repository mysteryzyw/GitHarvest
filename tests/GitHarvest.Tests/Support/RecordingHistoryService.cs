using GitHarvest.Core.History;

namespace GitHarvest.Tests.Support;

/// <summary>
/// 记录型导出历史桩：编排测试只关心「成功导出时写了什么、其余结论有没有写」，
/// 不碰磁盘。真实的 JSONL 读写与统计由 <c>HistoryServiceTests</c> 用真实临时文件覆盖。
/// </summary>
internal sealed class RecordingHistoryService : IHistoryService
{
    /// <summary>按追加顺序记录的条目。</summary>
    public List<ExportHistoryEntry> Entries { get; } = [];

    public void Append(ExportHistoryEntry entry) => Entries.Add(entry);

    /// <summary>桩的统计：条数即记录数，最常用分支取最后一条（首页统计的真实算法不走这里）。</summary>
    public ExportHistoryStatistics GetStatistics()
        => new(Entries.Count, Entries.Count == 0 ? null : Entries[^1].BranchName);

    public DateTimeOffset? GetLastExportedAt(string repositoryPath)
        => Entries.Count == 0 ? null : Entries[^1].ExportedAt;
}
