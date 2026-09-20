namespace GitHarvest.Core.Git;

/// <summary>
/// 双点范围 base..head 提交计数的统计结果：成功（携带数量，0 是合法结论——
/// 基准与 Head 为同一提交）或失败（失败类别 + 给用户看的中文提示）。
/// 与 <see cref="AncestorCheckResult"/> 同一风格。
/// </summary>
public sealed record CommitCountResult
{
    private CommitCountResult(
        int? count,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Count = count;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否统计成功（<see cref="Count"/> 非空；0 表示范围内没有提交）。</summary>
    public bool IsSuccess => Count is not null;

    /// <summary>范围内的提交数；失败时为 <see langword="null"/>。</summary>
    public int? Count { get; }

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造统计成功的结果（范围内没有提交时传 0）。</summary>
    public static CommitCountResult Succeeded(int count)
        => new(count, CommitListFailure.None, null, null);

    /// <summary>构造统计失败的结果。</summary>
    public static CommitCountResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(null, failure, failureMessage, technicalDetail);
    }
}
