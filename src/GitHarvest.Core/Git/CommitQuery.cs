namespace GitHarvest.Core.Git;

/// <summary>
/// 提交列表的一次查询条件：<see cref="Search"/> 是「哈希 / 提交信息 / 作者」三者皆匹配的
/// 过滤词（空或空白表示不过滤），<see cref="Offset"/> 与 <see cref="Limit"/> 是
/// <b>过滤后结果集</b>上的分页切片——搜索时翻页从过滤结果的第 <see cref="Offset"/> 条继续，
/// 与不过滤时的语义一致，调用方无感知差异。
/// </summary>
/// <param name="Search">过滤词；按提交信息、作者与哈希匹配（大小写不敏感）。</param>
/// <param name="Offset">过滤后结果集的起始偏移。</param>
/// <param name="Limit">本页最多返回的提交数。</param>
public sealed record CommitQuery(string? Search, int Offset, int Limit)
{
    /// <summary>构造条件校验：偏移与页大小必须是正数，否则是调用方的编程错误。</summary>
    public bool IsValid => Offset >= 0 && Limit > 0;
}
