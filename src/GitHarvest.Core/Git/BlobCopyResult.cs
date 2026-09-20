namespace GitHarvest.Core.Git;

/// <summary>
/// 取某个 blob 内容到目标流的结果。与既有结果对象同一风格：失败不抛异常、提示随结果走。
/// 失败时目标流里可能已经写入了半截内容——调用方负责按自己的口径清理
/// （导出的口径是整包删除，见 ExportService）。
/// </summary>
public sealed record BlobCopyResult
{
    private BlobCopyResult(CommitListFailure failure, string? failureMessage, string? technicalDetail)
    {
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否成功取到内容。</summary>
    public bool IsSuccess => Failure == CommitListFailure.None;

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造成功的结果。</summary>
    public static BlobCopyResult Succeeded() => new(CommitListFailure.None, null, null);

    /// <summary>构造失败的结果。</summary>
    public static BlobCopyResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(failure, failureMessage, technicalDetail);
    }
}
