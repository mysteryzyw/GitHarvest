namespace GitHarvest.Core.Git;

/// <summary>
/// <c>git diff --numstat -z</c> 的一条记录：增删行数与路径（重命名 / 复制带旧新两个路径）。
/// 二进制文件的两个数字都是 <see langword="null"/>（git 以 "-" 占位）。
/// </summary>
/// <param name="Additions">新增行数；二进制为 <see langword="null"/>。</param>
/// <param name="Deletions">删除行数；二进制为 <see langword="null"/>。</param>
/// <param name="Path">第一个路径（重命名 / 复制时为旧路径）。</param>
/// <param name="NewPath">第二个路径；仅重命名 / 复制时非空。</param>
public sealed record NumstatRecord(
    int? Additions,
    int? Deletions,
    string Path,
    string? NewPath)
{
    /// <summary>当前路径（重命名 / 复制取新路径）——与 <see cref="RawChangeRecord.CurrentPath"/> 同键关联。</summary>
    public string CurrentPath => NewPath ?? Path;
}
