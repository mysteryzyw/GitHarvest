namespace GitHarvest.Core.Git;

/// <summary>「拉取」（git fetch）的失败类别。</summary>
public enum FetchFailure
{
    /// <summary>无失败（成功）。</summary>
    None,

    /// <summary>仓库没有配置任何远程，无从拉取。</summary>
    NoRemote,

    /// <summary>仓库目录不存在（打开后被删除或移动）。</summary>
    DirectoryNotFound,

    /// <summary>git.exe 不可用（尚未探测成功或环境变化）。</summary>
    GitUnavailable,

    /// <summary>fetch 本身失败（网络不通、凭据被拒、远程损坏等）。</summary>
    GitError,
}
