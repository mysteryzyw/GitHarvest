namespace GitHarvest.Core.Git;

/// <summary>
/// 导出前总预览的统计汇总：变更文件全集 + 按变更类型（五类）的计数与占比。
/// 纯函数汇总，不接触 git 与文件系统；界面（壳）的统计卡、类型过滤 chip 与分组
/// 标题都从这里取数，保证「分组的数字」只有一份实现。
/// </summary>
public sealed record ChangeRangeSummary(IReadOnlyList<ChangedFile> Files)
{
    /// <summary>变更文件总数。</summary>
    public int TotalCount => Files.Count;

    /// <summary>变更范围内是否没有任何文件变化（禁止导出的依据之一）。</summary>
    public bool IsEmpty => Files.Count == 0;

    /// <summary>某一变更类型的文件数。</summary>
    public int CountOf(ChangeKind kind) => Count(kind, Files);

    /// <summary>
    /// 某一变更类型的文件数占总数的百分比（四舍五入到整数，与原型 <c>toFixed(0)</c> 一致）；
    /// 总数为 0 时为 0。
    /// </summary>
    public int PercentOf(ChangeKind kind)
        => TotalCount == 0
            ? 0
            : (int)Math.Round(CountOf(kind) * 100.0 / TotalCount, MidpointRounding.AwayFromZero);

    /// <summary>某一变更类型的全部文件（预览页分组清单的取数入口）。</summary>
    public IReadOnlyList<ChangedFile> FilesOf(ChangeKind kind)
        => [.. Files.Where(file => file.Kind == kind)];

    private static int Count(ChangeKind kind, IReadOnlyList<ChangedFile> files)
        => files.Count(file => file.Kind == kind);
}
