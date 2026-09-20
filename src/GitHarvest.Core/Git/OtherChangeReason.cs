namespace GitHarvest.Core.Git;

/// <summary>
/// 「其他」类变更的具体原因（spec 用户故事 28：类型变更 / 复制 / 未合并 / 子模块指针
/// 归入「其他」类型并在清单中可见——可见就要能说出为什么）。
/// </summary>
public enum OtherChangeReason
{
    /// <summary>不属于「其他」类（新增 / 删除 / 修改 / 重命名）时使用。</summary>
    None,

    /// <summary>类型变更（T）：符号链接、普通文件与子模块之间的类型切换。</summary>
    TypeChange,

    /// <summary>复制（C）：新路径的内容从已有路径复制而来（git -C 检测；本应用只开 -M，
    /// 正常不会出现，保留映射以防仓库配置开启了复制检测）。</summary>
    Copied,

    /// <summary>未合并（U）：差异中携带冲突标记的路径（树对树差异正常不会出现，保留映射）。</summary>
    Unmerged,

    /// <summary>子模块指针变化：路径的模式是 160000（gitlink），变化的是指向的提交而非文件内容。</summary>
    SubmodulePointer,

    /// <summary>无法识别的状态字母：宁可归入「其他」显式展示，也不悄悄丢掉一条变化。</summary>
    Unknown,
}
