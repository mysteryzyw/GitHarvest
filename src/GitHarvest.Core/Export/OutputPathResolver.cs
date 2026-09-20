namespace GitHarvest.Core.Export;

/// <summary>
/// 输出路径的解析（spec 用户故事 43）：优先级「本次 &gt; 每仓库上次 &gt; 全局默认」。
/// 三级取值都可能为空（用户从未配置过），解析结果可能是空串——那时界面应引导用户先选一个目录。
/// 纯函数，界面各页共用同一份实现，避免「记住的上次输出路径」在几处各写一遍判断。
/// </summary>
public static class OutputPathResolver
{
    /// <summary>按优先级取第一个非空值。</summary>
    /// <param name="sessionPath">
    /// 「本次」的临时值（页面上改成、但**不写回**每仓库记忆的目的地）。
    /// ticket 08 的交接特意提醒过：不要把每仓库记住的 <c>LastOutputPath</c> 当成「本次」传进来。
    /// </param>
    /// <param name="lastOutputPath">该仓库上次使用的输出路径（每仓库状态里的值）。</param>
    /// <param name="defaultOutputPath">全局设置里的默认输出路径。</param>
    /// <returns>解析出的输出路径；三级都为空时返回空串。</returns>
    public static string Resolve(string? sessionPath, string? lastOutputPath, string? defaultOutputPath)
    {
        if (!string.IsNullOrWhiteSpace(sessionPath))
        {
            return sessionPath;
        }

        if (!string.IsNullOrWhiteSpace(lastOutputPath))
        {
            return lastOutputPath;
        }

        return defaultOutputPath ?? string.Empty;
    }
}
