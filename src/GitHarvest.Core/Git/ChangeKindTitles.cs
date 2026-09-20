namespace GitHarvest.Core.Git;

/// <summary>
/// 变更类型与「其他」原因的界面文案（单一来源）：预览页的分组、更新说明的清单小节
/// 都从这里取标题，避免同一批中文词在界面与 Core 各写一遍、改一处漏一处。
/// </summary>
public static class ChangeKindTitles
{
    /// <summary>变更类型的中文标题（新增 / 删除 / 修改 / 重命名 / 其他）。</summary>
    /// <param name="kind">变更类型。</param>
    public static string For(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "新增",
        ChangeKind.Deleted => "删除",
        ChangeKind.Modified => "修改",
        ChangeKind.Renamed => "重命名",
        ChangeKind.Other => "其他",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的变更类型。"),
    };

    /// <summary>「其他」类具体原因的中文标题（spec 用户故事 28：在清单中可见）。</summary>
    /// <param name="reason">具体原因；<see cref="OtherChangeReason.None"/> 返回空串。</param>
    public static string For(OtherChangeReason reason) => reason switch
    {
        OtherChangeReason.None => string.Empty,
        OtherChangeReason.TypeChange => "类型变更",
        OtherChangeReason.Copied => "复制",
        OtherChangeReason.Unmerged => "未合并",
        OtherChangeReason.SubmodulePointer => "子模块指针",
        OtherChangeReason.Unknown => "未知变更",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知的变更原因。"),
    };
}
