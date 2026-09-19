using GitHarvest.Core.Git;

namespace GitHarvest.Core.Interaction;

/// <summary>
/// 当前打开的仓库会话状态：工作流各步共享「正在对哪个仓库操作」。
/// 仓库页（第 1 步）打开成功后写入，选择提交（第 2 步）及后续步骤读取；
/// 尚未打开仓库时为 <see langword="null"/>。
/// 会话状态是内存态（应用退出即失效），持久化的部分（最近列表、上次分支）走
/// <see cref="Settings.ISettingsService"/>，各归其位。实现由壳以单例提供，
/// 接口留在 Core 以守住「ViewModel 只依赖 Core 接口」的边界。
/// </summary>
public interface IRepositorySession
{
    /// <summary>当前打开的仓库；尚未打开时为 <see langword="null"/>。</summary>
    RepositoryInfo? OpenedRepository { get; set; }
}
