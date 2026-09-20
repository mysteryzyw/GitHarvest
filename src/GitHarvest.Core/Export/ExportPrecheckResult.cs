using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// 导出前预检算出的更新包结构：最终目录名与两侧文件数。
/// 目录名可能与请求的不同——重名时编排层会追加 <c>_HHmmss</c>（用户故事 31），
/// 界面据此提前告知用户实际会落到的位置。
/// </summary>
/// <param name="PackagePath">更新包的最终目录（输出路径 + 实际目录名）。</param>
/// <param name="FolderName">实际使用的更新日期目录名。</param>
/// <param name="BeforeFileCount">「更新前」文件夹将包含的文件数。</param>
/// <param name="AfterFileCount">「更新后」文件夹将包含的文件数。</param>
/// <param name="AddedFileCount">变更范围内新增的文件数。</param>
/// <param name="DeletedFileCount">变更范围内删除的文件数。</param>
public sealed record UpdatePackagePlan(
    string PackagePath,
    string FolderName,
    int BeforeFileCount,
    int AfterFileCount,
    int AddedFileCount,
    int DeletedFileCount);

/// <summary>
/// 导出前预检的结果，四种结论互斥：就绪（可以导出）、空范围（拒绝导出，用户故事 30）、
/// 文件系统冲突（中止并给出清单，用户故事 32）、失败（请求非法或 Git 访问失败）。
/// 预检**不写出任何文件**——界面在用户按下导出前就能给出结构、实际目录名与冲突清单。
/// </summary>
public sealed record ExportPrecheckResult
{
    private ExportPrecheckResult(
        UpdatePackagePlan? plan,
        IReadOnlyList<FileSystemConflict>? conflicts,
        ChangeRangeSummary? summary,
        bool isEmptyRange,
        ExportFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Plan = plan;
        Conflicts = conflicts;
        Summary = summary;
        IsEmptyRange = isEmptyRange;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>是否就绪：结构算出来了且没有冲突（可以导出）。</summary>
    public bool IsReady => Plan is not null && Conflicts is null;

    /// <summary>变更范围内没有任何文件变更（拒绝导出）。</summary>
    public bool IsEmptyRange { get; }

    /// <summary>是否因文件系统冲突而被拦下（清单在 <see cref="Conflicts"/>）。</summary>
    public bool IsBlockedByConflicts => Conflicts is not null;

    /// <summary>是否失败（请求非法 / Git 访问失败）。</summary>
    public bool IsFailed => Failure != ExportFailure.None;

    /// <summary>更新包结构（就绪或被冲突拦下时非空，界面仍可展示「将会包含什么」）。</summary>
    public UpdatePackagePlan? Plan { get; }

    /// <summary>文件系统冲突清单（<see cref="IsBlockedByConflicts"/> 时非空）。</summary>
    public IReadOnlyList<FileSystemConflict>? Conflicts { get; }

    /// <summary>
    /// 变更范围汇总（就绪或被冲突拦下时非空）：更新说明页据此渲染模板生成说明草稿，
    /// 与预览页、最终写出的说明同一份数字；空范围或失败时为 <see langword="null"/>。
    /// </summary>
    public ChangeRangeSummary? Summary { get; }

    /// <summary>失败类别；其余结论为 <see cref="ExportFailure.None"/>。</summary>
    public ExportFailure Failure { get; }

    /// <summary>给用户看的中文提示；就绪时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造「就绪」的结果。</summary>
    /// <param name="plan">更新包结构。</param>
    /// <param name="summary">变更范围汇总（更新说明页生成说明草稿用）。</param>
    public static ExportPrecheckResult Ready(UpdatePackagePlan plan, ChangeRangeSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new(plan, conflicts: null, summary, isEmptyRange: false, ExportFailure.None, null, null);
    }

    /// <summary>构造「空范围」的结果（可能同时带着结构：空范围时结构数字全为 0）。</summary>
    public static ExportPrecheckResult EmptyRange()
        => new(
            plan: null,
            conflicts: null,
            summary: null,
            isEmptyRange: true,
            ExportFailure.None,
            "变更范围内没有任何文件变更，没有可导出的内容；请回到第 2 步重新选择基准提交与 Head 提交。",
            technicalDetail: null);

    /// <summary>构造「文件系统冲突」的结果（结构照常给出，便于界面展示「本可包含什么」）。</summary>
    /// <param name="plan">更新包结构。</param>
    /// <param name="conflicts">冲突清单。</param>
    /// <param name="summary">变更范围汇总（更新说明页生成说明草稿用）。</param>
    public static ExportPrecheckResult Blocked(
        UpdatePackagePlan plan,
        IReadOnlyList<FileSystemConflict> conflicts,
        ChangeRangeSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(conflicts);

        return new(
            plan,
            conflicts,
            summary,
            isEmptyRange: false,
            ExportFailure.None,
            $"导出前检查发现 {conflicts.Count} 处文件系统冲突，已禁止导出：请先在仓库里处理下列问题，再重新导出。",
            technicalDetail: null);
    }

    /// <summary>构造失败的结果。</summary>
    public static ExportPrecheckResult Failed(
        ExportFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == ExportFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(null, conflicts: null, summary: null, isEmptyRange: false, failure, failureMessage, technicalDetail);
    }
}
