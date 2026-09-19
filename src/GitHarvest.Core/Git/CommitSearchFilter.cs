namespace GitHarvest.Core.Git;

/// <summary>
/// 提交搜索的匹配规则：「哈希 / 提交信息首行 / 作者」任一命中，大小写不敏感；
/// 空白关键词视为不过滤。这是<strong>唯一</strong>的匹配实现——Core 的
/// <see cref="GitService.GetCommitsAsync"/>（全量读取后的内存过滤）与界面的
/// 「输入即刻用已加载项过滤」共用它，两处语义不会漂移。
/// </summary>
public static class CommitSearchFilter
{
    /// <summary>
    /// 单个提交是否匹配关键词（关键词会被去掉首尾空白；空白关键词视为全部匹配）。
    /// </summary>
    /// <param name="commit">待判断的提交。</param>
    /// <param name="keyword">搜索关键词。</param>
    public static bool Matches(CommitSummary commit, string? keyword)
    {
        ArgumentNullException.ThrowIfNull(commit);

        var search = keyword?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            return true;
        }

        return commit.ShortHash.Contains(search, StringComparison.OrdinalIgnoreCase)
            || commit.Subject.Contains(search, StringComparison.OrdinalIgnoreCase)
            || commit.AuthorName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按关键词过滤一串提交，保持原有顺序（列表语义：新提交在前）。</summary>
    /// <param name="commits">待过滤的提交（原顺序保留）。</param>
    /// <param name="keyword">搜索关键词；空白时原样返回全部。</param>
    public static IReadOnlyList<CommitSummary> Filter(IEnumerable<CommitSummary> commits, string? keyword)
    {
        ArgumentNullException.ThrowIfNull(commits);

        return [.. commits.Where(commit => Matches(commit, keyword))];
    }
}
