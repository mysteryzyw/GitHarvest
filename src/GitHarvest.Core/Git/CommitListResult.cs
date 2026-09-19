namespace GitHarvest.Core.Git;

/// <summary>
/// 读取提交列表（一页）的结果：成功（携带提交摘要与是否有下一页）或失败
/// （失败类别 + 给用户看的中文提示）。与 <see cref="BranchListResult"/> 同一风格：
/// 失败以结果对象而非异常表达，取消仍走异常。
/// </summary>
public sealed record CommitListResult
{
    private CommitListResult(
        IReadOnlyList<CommitSummary>? commits,
        bool hasMore,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Commits = commits;
        HasMore = hasMore;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否读取成功。</summary>
    public bool IsSuccess => Commits is not null;

    /// <summary>
    /// 本页的提交摘要（新提交在前）；失败时为 <see langword="null"/>。
    /// 空列表表示过滤后没有提交（或仓库还没有提交）。
    /// </summary>
    public IReadOnlyList<CommitSummary>? Commits { get; }

    /// <summary>过滤后结果集里是否还有更多提交（驱动界面的滚动增量加载）。</summary>
    public bool HasMore { get; }

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造读取成功的结果。</summary>
    public static CommitListResult Succeeded(IReadOnlyList<CommitSummary> commits, bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(commits);

        return new CommitListResult(commits, hasMore, CommitListFailure.None, null, null);
    }

    /// <summary>构造读取失败的结果。</summary>
    public static CommitListResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new CommitListResult(null, hasMore: false, failure, failureMessage, technicalDetail);
    }
}
