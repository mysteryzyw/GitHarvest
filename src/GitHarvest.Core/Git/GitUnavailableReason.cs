namespace GitHarvest.Core.Git;

/// <summary>
/// git 环境不可用的原因。可用时取 <see cref="None"/>。
/// </summary>
public enum GitUnavailableReason
{
    /// <summary>git 可用。</summary>
    None = 0,

    /// <summary>三级探测都没找到 git.exe。</summary>
    NotFound = 1,

    /// <summary>找到了 git.exe，但都无法通过 <c>git --version</c> 验证。</summary>
    NotRunnable = 2,
}
