namespace GitHarvest.Core.Git;

/// <summary>
/// git 环境自检：应用启动与首页都要知道「本机有没有可用的 git，是哪一个，版本多少」。
/// 结果被缓存，界面层的重复询问不会反复启动进程；设置改变后用 <see cref="RefreshAsync"/> 重新验证。
/// </summary>
public interface IGitEnvironmentService
{
    /// <summary>取自检结果（首次调用真正探测，之后返回缓存）。</summary>
    Task<GitEnvironmentStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>强制重新探测（例如在「全局设置」里改过 git.exe 路径之后）。</summary>
    Task<GitEnvironmentStatus> RefreshAsync(CancellationToken cancellationToken = default);
}
