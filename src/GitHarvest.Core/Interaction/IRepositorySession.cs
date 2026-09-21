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

    /// <summary>
    /// 当前选定的变更范围（第 2 步写入，第 3 步「导出前总预览」与第 4 步读取）；
    /// 尚未选齐基准与 Head 时为 <see langword="null"/>。
    /// </summary>
    RangeSelection? SelectedRange { get; set; }

    /// <summary>
    /// 更新说明的本次草稿（第 4 步编辑器写入，spec 用户故事 40「只影响本次导出」）：
    /// 在页面间来回切换时保留用户的编辑；范围或仓库变更后由实现清空（草稿对应的范围已变，
    /// 再恢复旧稿会张冠李戴）。尚未生成过草稿时为 <see langword="null"/>。
    /// </summary>
    string? NotesDraft { get; set; }

    /// <summary>
    /// 本次导出的输出路径（第 3 步行内更改写入，spec 用户故事 23）。
    /// 它是「本次」档位：只影响本次会话，**不写回**每仓库记忆——随手试一个路径不该改掉
    /// 该仓库记住的目的地；每仓库记忆只在本仓库**导出成功后**刷新（用户故事 43）。
    /// 换仓库后由实现清空；未在页面改过时为 <see langword="null"/>（此时按记忆 / 全局默认解析）。
    /// </summary>
    string? SessionOutputPath { get; set; }
}
