namespace GitHarvest.Core.Git;

/// <summary>
/// 一个候选的 git.exe：可执行文件的绝对路径，以及它在哪一级被探测到。
/// 候选只代表「路径存在」，是否真的能执行由环境自检（<see cref="GitEnvironmentService"/>）逐个验证。
/// </summary>
/// <param name="ExecutablePath">git.exe 的绝对路径。</param>
/// <param name="Source">探测来源。</param>
public sealed record GitExecutableCandidate(string ExecutablePath, GitExecutableSource Source);
