namespace GitHarvest.Core.Git;

/// <summary>提交列表 / 提交详情读取共用的失败类别（与分支列表的失败分类对齐）。</summary>
public enum CommitListFailure
{
    /// <summary>无失败（成功）。</summary>
    None,

    /// <summary>仓库目录不存在（打开后被删除或移动）。</summary>
    DirectoryNotFound,

    /// <summary>git.exe 不可用（尚未探测成功或环境变化）。</summary>
    GitUnavailable,

    /// <summary>引用（分支名 / 提交哈希）在仓库里不存在——分支被删、哈希打错等。</summary>
    UnknownReference,

    /// <summary>git 命令本身失败（输出无法解析、仓库损坏等）。</summary>
    GitError,
}
