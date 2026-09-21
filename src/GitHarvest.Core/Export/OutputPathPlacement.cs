using GitHarvest.Core.Infrastructure;

namespace GitHarvest.Core.Export;

/// <summary>
/// 输出路径与仓库工作区的位置关系（spec 用户故事 33）：输出路径落在当前仓库工作区内时，
/// 导出的更新包会污染 <c>git status</c>——界面据此给出一次性警告（可继续）。
/// 判断只看路径本身，不碰文件系统。判定逻辑与「数据目录迁移的目标校验」共用
/// <see cref="Infrastructure.PathRelation"/>，两处对「子路径」的理解必须一致。
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
        => PathRelation.IsSameOrInside(outputPath, repositoryRoot);
}
