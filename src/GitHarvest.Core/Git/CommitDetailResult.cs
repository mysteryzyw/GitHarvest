namespace GitHarvest.Core.Git;

/// <summary>
/// 读取单提交详情的结果：成功（携带 <see cref="CommitDetail"/>）或失败
/// （失败类别 + 给用户看的中文提示）。与 <see cref="CommitListResult"/> 同一风格。
/// </summary>
public sealed record CommitDetailResult
{
    private CommitDetailResult(
        CommitDetail? detail,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Detail = detail;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否读取成功。</summary>
    public bool IsSuccess => Detail is not null;

    /// <summary>提交详情；失败时为 <see langword="null"/>。</summary>
    public CommitDetail? Detail { get; }

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造读取成功的结果。</summary>
    public static CommitDetailResult Succeeded(CommitDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new CommitDetailResult(detail, CommitListFailure.None, null, null);
    }

    /// <summary>构造读取失败的结果。</summary>
    public static CommitDetailResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new CommitDetailResult(null, failure, failureMessage, technicalDetail);
    }
}
