namespace GitHarvest.Core.Git;

/// <summary>
/// 变更范围内的一个文件条目：当前路径、变更类型与预览展示所需的数字
/// （+行数 / 大小 / 相似度）。导出编排（ticket 09）在此基础上取新旧 blob 做快照。
/// </summary>
/// <param name="Path">当前路径（仓库相对、正斜杠风格）；重命名 / 复制时为新路径。</param>
/// <param name="OldPath">旧路径；仅重命名 / 复制时非空。</param>
/// <param name="Kind">变更类型（五类之一）。</param>
/// <param name="OtherReason"><see cref="ChangeKind.Other"/> 的具体原因；其余类型为 <see cref="OtherChangeReason.None"/>。</param>
/// <param name="Additions">新增行数；二进制文件为 <see langword="null"/>（git numstat 以 "-" 占位）。</param>
/// <param name="Deletions">删除行数；二进制文件为 <see langword="null"/>。</param>
/// <param name="SimilarityPercent">重命名 / 复制的相似度百分比（0–100）；其余类型为 <see langword="null"/>。</param>
/// <param name="SizeBytes">
/// 预览展示的文件大小（字节）：重命名 / 复制 / 新增 / 修改取「更新后」版本，
/// 删除取「更新前」版本；子模块指针（gitlink）没有文件内容，为 <see langword="null"/>。
/// </param>
/// <param name="OldBlobHash">「更新前」版本的 blob 完整哈希；新增时为 <see langword="null"/>。</param>
/// <param name="NewBlobHash">「更新后」版本的 blob 完整哈希；删除时为 <see langword="null"/>。</param>
public sealed record ChangedFile(
    string Path,
    string? OldPath,
    ChangeKind Kind,
    OtherChangeReason OtherReason,
    int? Additions,
    int? Deletions,
    int? SimilarityPercent,
    long? SizeBytes,
    string? OldBlobHash,
    string? NewBlobHash)
{
    /// <summary>是否携带行数统计（二进制文件没有）。</summary>
    public bool HasLineCounts => Additions.HasValue && Deletions.HasValue;
}
