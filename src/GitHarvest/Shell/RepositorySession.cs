using GitHarvest.Core.Git;
using GitHarvest.Core.Interaction;

namespace GitHarvest.Shell;

/// <summary>
/// <see cref="IRepositorySession"/> 的壳实现：DI 单例，纯内存会话状态，应用退出即失效。
/// 无属性变更通知——页面与 ViewModel 都是 Transient，每次导航新建时读取当前值即可。
/// </summary>
public sealed class RepositorySession : IRepositorySession
{
    /// <inheritdoc />
    public RepositoryInfo? OpenedRepository { get; set; }

    /// <inheritdoc />
    public RangeSelection? SelectedRange { get; set; }
}
