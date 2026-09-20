namespace GitHarvest.Core.Export;

/// <summary>
/// 输出路径与仓库工作区的位置关系（spec 用户故事 33）：输出路径落在当前仓库工作区内时，
/// 导出的更新包会污染 <c>git status</c>——界面据此给出一次性警告（可继续）。
/// 判断只看路径本身，不碰文件系统。
/// </summary>
public static class OutputPathPlacement
{
    /// <summary>
    /// 输出路径是否位于仓库工作区内（等于仓库根也算落在里面）。
    /// </summary>
    /// <param name="outputPath">输出路径（用户选择的目录）。</param>
    /// <param name="repositoryRoot">仓库根目录（bare 仓库没有工作区，调用方不应调用本方法）。</param>
    /// <returns>落在里面返回真；路径无法解析（非法字符等）时按「不在里面」处理，不打断导出。</returns>
    public static bool IsInsideRepository(string outputPath, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return false;
        }

        try
        {
            var root = Normalize(repositoryRoot);
            var target = Normalize(outputPath);

            // 两侧都补上结尾分隔符再比较前缀，避免「D:\repo-other」被误判为在「D:\repo」里。
            return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 用户输入的路径可能含非法字符：这类路径连解析都过不了，交给后续的创建/写出报错。
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
