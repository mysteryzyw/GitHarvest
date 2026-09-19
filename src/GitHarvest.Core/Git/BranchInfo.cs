namespace GitHarvest.Core.Git;

/// <summary>
/// 一个分支候选项：本地分支或远程跟踪分支（<see cref="IsRemote"/> 区分，界面据此分组），
/// 附带其最新提交摘要作为预览。游离头指针（detached HEAD）也表达为一个候选项
/// （<see cref="IsDetached"/> 为真，<see cref="Name"/> 固定为 "HEAD"），
/// 由界面标注「（游离）」；它总是排在列表最前。
/// </summary>
/// <param name="Name">分支短名（本地 <c>main</c>、远程 <c>origin/main</c>；游离项为 "HEAD"）。</param>
/// <param name="IsRemote">是否远程跟踪分支（origin/ 前缀那一组）。</param>
/// <param name="IsCurrent">是否当前检出的分支。</param>
/// <param name="IsDetached">是否游离头指针候选项（此时 <see cref="IsCurrent"/> 必为假）。</param>
/// <param name="Tip">该分支最新提交的摘要。</param>
public sealed record BranchInfo(string Name, bool IsRemote, bool IsCurrent, bool IsDetached, CommitSummary Tip);
