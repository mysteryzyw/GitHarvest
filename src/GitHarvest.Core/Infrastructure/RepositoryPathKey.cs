namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 仓库路径的归一化键：把用户用各种写法给出的同一个仓库（大小写、正/反斜杠、结尾分隔符、
/// 相对路径）收敛成同一个字符串，供「按仓库索引」的数据结构使用——每仓库状态
/// （repository-state.json）与导出历史（export-history.jsonl）都以它为键，
/// 因此两处必须完全一致：同一个仓库在设置里记住的分支与在历史里匹配到的记录不能各认一套写法。
/// 大小写差异不在键里处理，而由使用方的比较器吸收（Win32 路径本就大小写不敏感）。
/// </summary>
public static class RepositoryPathKey
{
    /// <summary>
    /// 归一化仓库路径：完整路径形式、去掉结尾分隔符。
    /// 路径本身非法（空串）时抛出 <see cref="ArgumentException"/>——这是调用方的编程错误。
    /// 具体归一化规则与「数据目录路径」共用 <see cref="PathRelation.NormalizeDirectory"/>：
    /// 同一台机器上的目录路径，不该有两套「什么算同一个路径」的理解。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    public static string Normalize(string repositoryPath)
        => PathRelation.NormalizeDirectory(repositoryPath);

    /// <summary>容错版归一化：手改文件里的怪路径不值得让整份数据加载失败，返回 <see langword="null"/> 丢弃该条即可。</summary>
    /// <param name="repositoryPath">可能为空白或非法的路径。</param>
    public static string? TryNormalize(string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return null;
        }

        try
        {
            return Normalize(repositoryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
