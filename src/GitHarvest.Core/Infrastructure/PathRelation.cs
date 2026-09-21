namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 两个路径的位置关系判断（只看路径本身，不碰文件系统）。
/// 「一个路径是否在另一个里面」在库里有两处真实用途：输出路径落在仓库工作区内（用户故事 33，
/// 见 <see cref="Export.OutputPathPlacement"/>）与数据目录迁移的目标校验（不能把新目录选在当前数据目录里）。
/// 判定必须一致——两处各写一份前缀比较，早晚有一处忘了补结尾分隔符，
/// 于是「D:\repo-other」被当成「D:\repo」的子目录。因此收成这一个实现。
/// </summary>
internal static class PathRelation
{
    /// <summary>
    /// 把一个目录路径归一化成「完整路径 + 去掉结尾分隔符」。
    /// 两处需要它：「按目录做键/做相等比较」（尾斜杠不该产生假差异）与「存进配置文件的路径要干净」。
    /// 盘根（如 <c>C:\</c>）去掉分隔符会退化成 <c>C:</c>（相对当前目录的驱动器语义），因此截断结果
    /// 比盘根还短时保持原样。
    /// </summary>
    /// <param name="path">目录路径（可以是相对路径，会按当前目录解析）。</param>
    /// <returns>归一化后的完整路径（无结尾分隔符）。</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空白或无法解析。</exception>
    public static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;

        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length > 0 && trimmed.Length >= root.Length ? trimmed : fullPath;
    }

    /// <summary>
    /// <paramref name="path"/> 是否等于或位于 <paramref name="parent"/> 之内。
    /// 路径无法解析（非法字符、超长）时返回假——这类路径连比较都做不了，
    /// 由调用方的后续步骤去报错，不在这里抛。
    /// </summary>
    /// <param name="path">要判断的路径（完整或相对路径，内部会归一化）。</param>
    /// <param name="parent">父目录路径。</param>
    /// <returns>等于或位于其中返回真；任一参数为空或路径无法解析时返回假。</returns>
    public static bool IsSameOrInside(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        try
        {
            var root = Normalize(parent);
            var target = Normalize(path);

            // 两侧都补上结尾分隔符再比较前缀，避免「D:\repo-other」被误判为在「D:\repo」里。
            return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>归一成「完整路径 + 结尾分隔符」的形式，便于做前缀比较。</summary>
    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar)
            ? full
            : full + Path.DirectorySeparatorChar;
    }
}
