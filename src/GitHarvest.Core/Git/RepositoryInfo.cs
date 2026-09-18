namespace GitHarvest.Core.Git;

/// <summary>
/// 已打开仓库的概要信息（不可变值对象）。
/// 根路径一律是 Git 认定的仓库根（从仓库子目录打开时向上解析，而非用户传入的目录）；
/// bare 仓库没有工作区，根路径即仓库目录本身。
/// </summary>
/// <param name="RootPath">仓库根目录的绝对路径（Windows 风格分隔符）。</param>
/// <param name="IsBare">是否为 bare 仓库（无工作区）。</param>
/// <param name="CurrentBranch">当前检出的分支名；游离头指针（detached HEAD）时为 <see langword="null"/>。
/// bare 仓库的 HEAD 符号引用仍指向一个分支，此处返回该分支名。</param>
/// <param name="CommitCount">仓库可从 HEAD 到达的提交总数；空仓库为 0。</param>
public sealed record RepositoryInfo(
    string RootPath,
    bool IsBare,
    string? CurrentBranch,
    int CommitCount)
{
    /// <summary>
    /// 提交数是否足以构成「基准 + Head」：双点范围 <c>base..head</c> 要求基准必须是 Head 的祖先
    /// 且两者不同，因此至少需要 2 个提交；空仓库与单提交仓库都无法构成变更范围。
    /// </summary>
    public bool HasEnoughCommits => CommitCount >= 2;
}
