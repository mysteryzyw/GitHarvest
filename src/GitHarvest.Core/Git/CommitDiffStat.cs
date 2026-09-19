namespace GitHarvest.Core.Git;

/// <summary>
/// 一次提交的 diffstat 汇总：变更文件数与增删行数。
/// 二进制文件计入文件数但不计增删行数（git numstat 对二进制给出占位而非数字）。
/// </summary>
/// <param name="FilesChanged">变更文件总数。</param>
/// <param name="Additions">新增行数合计。</param>
/// <param name="Deletions">删除行数合计。</param>
public sealed record CommitDiffStat(int FilesChanged, int Additions, int Deletions);
