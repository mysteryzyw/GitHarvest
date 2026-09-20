namespace GitHarvest.Core.Git;

/// <summary>
/// 变更类型（CONTEXT.md）：变更范围内每个文件的分类，全集为五类——
/// 新增、删除、修改、重命名、其他。「其他」收编类型变更、复制、未合并与子模块指针，
/// 具体原因记录在 <see cref="ChangedFile.OtherReason"/>。
/// 分类的来源是 git 双点差异的状态字母（<c>git diff --raw</c>），映射规则在
/// <see cref="ChangeRangeParser"/>，界面只消费本枚举。
/// </summary>
public enum ChangeKind
{
    /// <summary>新增：仅出现在「更新后」的文件。</summary>
    Added,

    /// <summary>删除：仅出现在「更新前」的文件。</summary>
    Deleted,

    /// <summary>修改：更新前 / 更新后各一份快照。</summary>
    Modified,

    /// <summary>重命名：git -M 默认 50% 相似度阈值判定（阈值由 git 决定，这里只消费结论）。</summary>
    Renamed,

    /// <summary>其他：类型变更 / 复制 / 未合并 / 子模块指针变化。</summary>
    Other,
}
