namespace GitHarvest.Core.History;

/// <summary>
/// 导出历史：JSONL 追加与统计查询（spec 的 Core 服务划分 + 用户故事 45）。
/// <see cref="IExportService"/> 在更新包成功写出后调用 <see cref="Append"/>；
/// 首页概览与最近仓库卡片调用两个查询成员。ViewModels 只依赖本接口，不接触文件。
/// 容错口径：这是**辅助记录**，任何读写问题都不得影响导出主流程——一律记 Warning 后继续。
/// </summary>
public interface IHistoryService
{
    /// <summary>
    /// 追加一条导出记录并立即落盘（追加语义：文件只增不改，一条一行 JSON）。
    /// 写出失败降级为 Warning（内存快照仍保留本次记录），绝不向调用方抛出——
    /// 导出已经成功，不能因为记不下历史而让用户以为导出失败。
    /// </summary>
    /// <param name="entry">本次导出的记录。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException">记录缺少仓库路径（无法归属到任何仓库）。</exception>
    void Append(ExportHistoryEntry entry);

    /// <summary>首页概览统计：历史更新包数与最常用分支。</summary>
    ExportHistoryStatistics GetStatistics();

    /// <summary>
    /// 指定仓库最近一次导出的时间（最近仓库卡片的「上次导出」）；从未导出过时返回 <see langword="null"/>。
    /// 仓库路径的大小写、分隔符与结尾差异视为同一仓库。
    /// </summary>
    /// <param name="repositoryPath">仓库根路径。</param>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> 为空白。</exception>
    DateTimeOffset? GetLastExportedAt(string repositoryPath);
}
