using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// 导出前总预览的结果，三种结论互斥：
/// 成功（携带汇总，可能为空范围）、祖先校验不通过（分叉提交对，禁止导出）、
/// 流程失败（目录不存在 / git 不可用 / 引用不存在 / Git 错误）。
/// 与既有结果对象同一风格：失败不抛异常，提示随结果走。
/// </summary>
public sealed record ChangeRangePreviewResult
{
    private ChangeRangePreviewResult(
        ChangeRangeSummary? summary,
        bool isAncestryViolated,
        CommitListFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Summary = summary;
        IsAncestryViolated = isAncestryViolated;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否成功得到汇总（可能为空范围）。</summary>
    public bool IsSuccess => Summary is not null;

    /// <summary>祖先校验是否不通过（基准不是 Head 的祖先）。</summary>
    public bool IsAncestryViolated { get; }

    /// <summary>汇总结果；成功时非空（空范围即 <see cref="ChangeRangeSummary.IsEmpty"/>），其余结论为 <see langword="null"/>。</summary>
    public ChangeRangeSummary? Summary { get; }

    /// <summary>失败类别；成功与祖先违规时为 <see cref="CommitListFailure.None"/>。</summary>
    public CommitListFailure Failure { get; }

    /// <summary>给用户看的中文提示（失败原因 / 祖先违规的解释）；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造成功的结果（空范围传入空清单的汇总）。</summary>
    public static ChangeRangePreviewResult Succeeded(ChangeRangeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new(summary, isAncestryViolated: false, CommitListFailure.None, null, null);
    }

    /// <summary>构造「祖先校验不通过」的结果（分叉提交对，禁止导出）。</summary>
    public static ChangeRangePreviewResult AncestryViolated()
        => new(
            null,
            isAncestryViolated: true,
            CommitListFailure.None,
            "基准提交不是 Head 提交的祖先，两者不在同一祖先链上；请回到第 2 步重新选择，变更范围必须从 Head 的历史中选取。",
            null);

    /// <summary>构造流程失败的结果。</summary>
    public static ChangeRangePreviewResult Failed(
        CommitListFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == CommitListFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(null, isAncestryViolated: false, failure, failureMessage, technicalDetail);
    }
}
