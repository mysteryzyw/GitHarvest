namespace GitHarvest.Core.Git;

/// <summary>
/// 一个提交在列表场景下的摘要：短哈希、信息首行、作者与作者时间。
/// 分支列表（ticket 06）与提交列表（ticket 07）共用这一形态。
/// </summary>
/// <param name="ShortHash">短哈希（git 自动判定的无歧义长度，通常 7 位）。</param>
/// <param name="Subject">提交信息首行。</param>
/// <param name="AuthorName">作者名。</param>
/// <param name="AuthorTime">作者时间（带时区）。</param>
public sealed record CommitSummary(string ShortHash, string Subject, string AuthorName, DateTimeOffset AuthorTime);
