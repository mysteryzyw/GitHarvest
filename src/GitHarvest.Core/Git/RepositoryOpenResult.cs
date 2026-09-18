namespace GitHarvest.Core.Git;

/// <summary>
/// 打开仓库的结果：要么成功（携带 <see cref="RepositoryInfo"/>），要么失败
/// （携带失败类别与给用户看的中文提示）。两种状态互斥，经
/// <see cref="Succeeded"/> / <see cref="Failed"/> 构造。
/// 打开是「尝试 → 得到结果」的常规流程，因此失败以结果对象而非异常表达，
/// 便于界面直接呈现；取消（<see cref="OperationCanceledException"/>）仍走异常，由调用方收敛。
/// </summary>
public sealed record RepositoryOpenResult
{
    private RepositoryOpenResult(
        RepositoryInfo? repository,
        RepositoryOpenFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Repository = repository;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>仓库是否打开成功。</summary>
    public bool IsSuccess => Repository is not null;

    /// <summary>打开成功的仓库概要；失败时为 <see langword="null"/>。</summary>
    public RepositoryInfo? Repository { get; }

    /// <summary>失败类别；成功时为 <see cref="RepositoryOpenFailure.None"/>。</summary>
    public RepositoryOpenFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）；多数失败没有更多细节。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造打开成功的结果。</summary>
    public static RepositoryOpenResult Succeeded(RepositoryInfo repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return new RepositoryOpenResult(repository, RepositoryOpenFailure.None, null, null);
    }

    /// <summary>构造打开失败的结果。</summary>
    /// <param name="failure">失败类别，不能是 <see cref="RepositoryOpenFailure.None"/>。</param>
    /// <param name="failureMessage">给用户看的中文提示。</param>
    /// <param name="technicalDetail">面向日志的技术细节，可为空。</param>
    public static RepositoryOpenResult Failed(
        RepositoryOpenFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == RepositoryOpenFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new RepositoryOpenResult(null, failure, failureMessage, technicalDetail);
    }
}
