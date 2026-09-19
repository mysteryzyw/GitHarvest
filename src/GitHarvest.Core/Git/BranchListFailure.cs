namespace GitHarvest.Core.Git;

/// <summary>读取分支列表的失败类别（与打开仓库的失败分类对齐）。</summary>
public enum BranchListFailure
{
    /// <summary>无失败（成功）。</summary>
    None,

    /// <summary>仓库目录不存在（打开后被删除或移动）。</summary>
    DirectoryNotFound,

    /// <summary>git.exe 不可用（尚未探测成功或环境变化）。</summary>
    GitUnavailable,

    /// <summary>git 命令本身失败（输出无法解析、仓库损坏等）。</summary>
    GitError,
}
