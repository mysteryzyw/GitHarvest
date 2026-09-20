namespace GitHarvest.Core.Git;

/// <summary>
/// <c>git diff --raw -z</c> 的一条原始记录：两侧文件模式、blob 哈希、状态字母与路径。
/// 状态字母与 50% 重命名阈值的结论都在这条记录里，<see cref="ChangeRangeParser"/>
/// 负责把它映射成 <see cref="ChangedFile"/>。
/// </summary>
/// <param name="OldMode">「更新前」侧的文件模式（6 位八进制，如 100644 / 120000 / 160000）。</param>
/// <param name="NewMode">「更新后」侧的文件模式；新增时为 000000。</param>
/// <param name="OldBlobHash">「更新前」版本的 blob 完整哈希；新增（全零占位）时为 <see langword="null"/>。</param>
/// <param name="NewBlobHash">「更新后」版本的 blob 完整哈希；删除（全零占位）时为 <see langword="null"/>。</param>
/// <param name="StatusLetter">git 状态字母：A / D / M / R / C / T / U / X。</param>
/// <param name="Score">状态附带的相似度分数（R100 的 100）；无分数的字母为 <see langword="null"/>。</param>
/// <param name="Path">记录的第一个路径（重命名 / 复制时为旧路径）。</param>
/// <param name="NewPath">第二个路径；仅重命名 / 复制时非空。</param>
public sealed record RawChangeRecord(
    string OldMode,
    string NewMode,
    string? OldBlobHash,
    string? NewBlobHash,
    string StatusLetter,
    int? Score,
    string Path,
    string? NewPath)
{
    /// <summary>当前路径（重命名 / 复制取新路径，其余取原路径）——numstat 与大小表都以它为键关联。</summary>
    public string CurrentPath => NewPath ?? Path;

    /// <summary>是否涉及子模块（gitlink，模式 160000）：指针变化归「其他」而非「修改」。</summary>
    public bool InvolvesSubmodule =>
        OldMode == ChangeRangeParser.GitlinkMode || NewMode == ChangeRangeParser.GitlinkMode;
}
