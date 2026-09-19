namespace GitHarvest.Core.Git;

/// <summary>
/// 「拉取」（git fetch）的结果：成功（远程跟踪分支已更新，调用方随后重读分支数据）
/// 或失败（失败类别 + 给用户看的中文提示）。fetch 没有数据载荷，
/// 但沿用 <see cref="RepositoryOpenResult"/> 的结果对象风格：失败不抛异常，取消仍走异常。
/// </summary>
public sealed record FetchResult
{
    private FetchResult(bool isSuccess, FetchFailure failure, string? failureMessage, string? technicalDetail)
    {
        IsSuccess = isSuccess;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>fetch 是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>失败类别；成功时为 <see cref="FetchFailure.None"/>。</summary>
    public FetchFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造拉取成功的结果。</summary>
    public static FetchResult Succeeded() => new(true, FetchFailure.None, null, null);

    /// <summary>构造拉取失败的结果。</summary>
    public static FetchResult Failed(FetchFailure failure, string failureMessage, string? technicalDetail = null)
    {
        if (failure == FetchFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new FetchResult(false, failure, failureMessage, technicalDetail);
    }
}
