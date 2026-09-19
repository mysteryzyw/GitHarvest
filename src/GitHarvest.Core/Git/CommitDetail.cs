namespace GitHarvest.Core.Git;

/// <summary>
/// 一个提交的完整详情：供「选中单个提交查看完整预览」使用——
/// 完整提交信息（多行）、作者与时间、父提交与该次提交的 diffstat 汇总。
/// </summary>
/// <param name="Hash">完整哈希（40 位十六进制）。</param>
/// <param name="ShortHash">短哈希（git 自动判定的无歧义长度）。</param>
/// <param name="Subject">提交信息首行。</param>
/// <param name="FullMessage">完整提交信息（含标题之后的正文与换行）。</param>
/// <param name="AuthorName">作者名。</param>
/// <param name="AuthorTime">作者时间（带时区）。</param>
/// <param name="ParentHashes">父提交的完整哈希；根提交为空，普通提交一个，合并提交两个及以上。</param>
/// <param name="DiffStat">该次提交的 diffstat 汇总（合并提交按对第一父提交的变更计）。</param>
public sealed record CommitDetail(
    string Hash,
    string ShortHash,
    string Subject,
    string FullMessage,
    string AuthorName,
    DateTimeOffset AuthorTime,
    IReadOnlyList<string> ParentHashes,
    CommitDiffStat DiffStat);
