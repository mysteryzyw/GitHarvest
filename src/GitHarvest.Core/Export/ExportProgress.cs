namespace GitHarvest.Core.Export;

/// <summary>导出过程所处的阶段（界面据此给出进度文案）。</summary>
public enum ExportPhase
{
    /// <summary>校验请求与解析更新日期目录名。</summary>
    Preparing,

    /// <summary>计算变更范围（祖先校验 + 双点差异）。</summary>
    ComputingRange,

    /// <summary>导出前的文件系统冲突扫描。</summary>
    ScanningConflicts,

    /// <summary>写出「更新前 / 更新后」快照。</summary>
    WritingSnapshots,

    /// <summary>生成并写出更新说明.md。</summary>
    WritingNotes,
}

/// <summary>
/// 导出进度：阶段 + 已完成的步数（一步 = 写一个快照文件；更新说明算最后一步）。
/// 早期阶段（准备 / 算范围 / 扫描）步数未知，<see cref="TotalSteps"/> 为 0。
/// </summary>
/// <param name="Phase">当前阶段。</param>
/// <param name="CompletedSteps">已完成的步数。</param>
/// <param name="TotalSteps">总步数；未知时为 0。</param>
public sealed record ExportProgress(ExportPhase Phase, int CompletedSteps, int TotalSteps)
{
    /// <summary>完成百分比（0–100）；总步数未知时为 0。</summary>
    public int Percent => TotalSteps <= 0
        ? 0
        : (int)Math.Round(CompletedSteps * 100.0 / TotalSteps, MidpointRounding.AwayFromZero);
}
