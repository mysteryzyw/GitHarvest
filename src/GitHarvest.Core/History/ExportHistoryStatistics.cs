namespace GitHarvest.Core.History;

/// <summary>
/// 首页概览里由导出历史得出的统计（spec 用户故事 45）。
/// 另两张卡「最近仓库数」与「默认输出根目录」来自设置，不在这里——
/// 一处数据一个来源，首页只负责把两张来源拼起来。
/// </summary>
/// <param name="PackageCount">历史更新包数：导出历史里的记录条数。</param>
/// <param name="MostUsedBranch">最常用分支：导出次数最多的分支名；没有任何可统计的分支时为 <see langword="null"/>（首页显示「—」）。</param>
public sealed record ExportHistoryStatistics(int PackageCount, string? MostUsedBranch);
