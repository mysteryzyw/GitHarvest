namespace GitHarvest.Core.Git;

/// <summary>
/// 双点变更范围（base..head）的计算结果：成功（携带变更文件清单，可能为空——
/// 空范围是成功结论，禁止导出的判断由调用方做）或失败（失败类别 + 中文提示）。
/// 与 <see cref="CommitListResult"/> 同一风格。
/// </summary>
public sealed record ChangeRangeResult
{
    private ChangeRangeResult(
        IReadOnlyList<ChangedFile>? files,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Files = files;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否计算成功（<see cref="Files"/> 非空；清单可能为空列表）。</summary>
    public bool IsSuccess => Files is not null;

    /// <summary>变更范围内的全部文件；失败时为 <see langword="null"/>。</summary>
    public IReadOnlyList<ChangedFile>? Files { get; }

    /// <summary>失败类别；成功时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造计算成功的结果（空范围传入空列表）。</summary>
    public static ChangeRangeResult Succeeded(IReadOnlyList<ChangedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return new(files, CommitListFailure.None, null, null);
    }

    /// <summary>构造计算失败的结果。</summary>
    public static ChangeRangeResult Failed(
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
