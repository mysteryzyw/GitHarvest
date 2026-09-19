namespace GitHarvest.Core.Git;

/// <summary>
/// 读取分支列表的结果：成功（携带按「游离 → 本地 → 远程」排序的候选项）或失败
/// （失败类别 + 给用户看的中文提示）。与 <see cref="RepositoryOpenResult"/> 同一风格：
/// 失败以结果对象而非异常表达，取消仍走异常。
/// </summary>
public sealed record BranchListResult
{
    private BranchListResult(
        IReadOnlyList<BranchInfo>? branches,
        BranchListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Branches = branches;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否读取成功。</summary>
    public bool IsSuccess => Branches is not null;

    /// <summary>
    /// 分支候选项（游离项在前，其后本地分支、远程跟踪分支）；失败时为 <see langword="null"/>。
    /// 空列表表示仓库还没有任何分支引用（空仓库）。
    /// </summary>
    public IReadOnlyList<BranchInfo>? Branches { get; }

    /// <summary>失败类别；成功时为 <see cref="BranchListFailure.None"/>。</summary>
    public BranchListFailure Failure { get; }

    /// <summary>给用户看的中文提示；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造读取成功的结果。</summary>
    public static BranchListResult Succeeded(IReadOnlyList<BranchInfo> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        return new BranchListResult(branches, BranchListFailure.None, null, null);
    }

    /// <summary>构造读取失败的结果。</summary>
    public static BranchListResult Failed(
        BranchListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == BranchListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new BranchListResult(null, failure, failureMessage, technicalDetail);
    }
}
