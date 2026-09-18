namespace GitHarvest.Core.Git;

/// <summary>
/// 打开仓库失败的类别。成功时为 <see cref="None"/>；
/// 每个类别对应一种给用户的明确提示，文案由 <see cref="RepositoryOpenResult.FailureMessage"/> 携带。
/// </summary>
public enum RepositoryOpenFailure
{
    /// <summary>成功，无失败。</summary>
    None = 0,

    /// <summary>目标目录存在，但不是 Git 仓库（目录里没有 .git，父目录链上也没有）。</summary>
    NotARepository,

    /// <summary>目标目录不存在（常见于重开最近列表时仓库已被删除或移动）。</summary>
    DirectoryNotFound,

    /// <summary>git.exe 不可用（未找到或无法执行），仓库无法验证。</summary>
    GitUnavailable,

    /// <summary>Git 返回了其他错误（stderr 详情在 <see cref="RepositoryOpenResult.TechnicalDetail"/>）。</summary>
    GitError,
}
