namespace GitHarvest.Core.Git;

/// <summary>
/// 祖先校验的结果：基准提交是否 Head 提交的祖先（含同一提交）。
/// 「是 / 否」都是成功结论（<see cref="IsSuccess"/> 为真）；校验流程本身失败
/// （目录不存在、git 不可用、引用不存在等）以结果对象表达，与既有结果风格一致。
/// </summary>
public sealed record AncestorCheckResult
{
    private AncestorCheckResult(
        bool isSuccess,
        bool isAncestor,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        IsSuccess = isSuccess;
        IsAncestor = isAncestor;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否得到了确定的「是 / 否」结论。</summary>
    public bool IsSuccess { get; }

    /// <summary>基准提交是否 Head 提交的祖先；仅在 <see cref="IsSuccess"/> 为真时有意义。</summary>
    public bool IsAncestor { get; }

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造校验成功的结果（无论结论是 / 否）。</summary>
    public static AncestorCheckResult Succeeded(bool isAncestor)
        => new(isSuccess: true, isAncestor, CommitListFailure.None, null, null);

    /// <summary>构造校验失败的结果。</summary>
    public static AncestorCheckResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(isSuccess: false, isAncestor: false, failure, failureMessage, technicalDetail);
    }
}
