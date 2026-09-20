namespace GitHarvest.Core.Export;

/// <summary>导出的结论（四种互斥）。</summary>
public enum ExportOutcome
{
    /// <summary>成功产出更新包。</summary>
    Completed,

    /// <summary>变更范围内没有任何文件变更，拒绝导出（spec 用户故事 30：不会向客户交付空包）。</summary>
    EmptyRange,

    /// <summary>导出前扫描到文件系统冲突，中止导出（spec 用户故事 32），清单随结果返回。</summary>
    ConflictBlocked,

    /// <summary>其他失败：请求非法、Git 访问失败或文件系统写入失败。</summary>
    Failed,
}

/// <summary>导出失败的类别（<see cref="ExportOutcome.Failed"/> 时有效）。</summary>
public enum ExportFailure
{
    /// <summary>无失败（成功、空范围或冲突中止）。</summary>
    None,

    /// <summary>请求本身不合法：输出路径为空、更新日期目录名非法。</summary>
    InvalidRequest,

    /// <summary>Git 访问失败：变更范围算不出来、或某个文件的内容取不到。</summary>
    GitAccess,

    /// <summary>文件系统失败：创建目录或写出文件失败（磁盘满、权限不足、路径过长等）。</summary>
    FileSystem,
}

/// <summary>
/// 一次导出的结果。与既有结果对象同一风格：失败不抛异常、提示随结果走；
/// 方向与预览结果一致——「空范围」「文件系统冲突」都是**确定结论**而不是失败，
/// 各自带自己的字段（<see cref="Outcome"/> / <see cref="Conflicts"/>）。
/// 取消是唯一走异常的路径（与项目约定一致），且取消前会清理半成品目录。
/// </summary>
public sealed record ExportResult
{
    private ExportResult(
        ExportOutcome outcome,
        string? packagePath,
        int beforeFileCount,
        int afterFileCount,
        int addedFileCount,
        int deletedFileCount,
        long totalBytes,
        IReadOnlyList<FileSystemConflict>? conflicts,
        ExportFailure failure,
        string? failureMessage,
        string? technicalDetail)
    {
        Outcome = outcome;
        PackagePath = packagePath;
        BeforeFileCount = beforeFileCount;
        AfterFileCount = afterFileCount;
        AddedFileCount = addedFileCount;
        DeletedFileCount = deletedFileCount;
        TotalBytes = totalBytes;
        Conflicts = conflicts;
        Failure = failure;
        FailureMessage = failureMessage;
        TechnicalDetail = technicalDetail;
    }

    /// <summary>结论。</summary>
    public ExportOutcome Outcome { get; }

    /// <summary>是否成功产出更新包。</summary>
    public bool IsSuccess => Outcome == ExportOutcome.Completed;

    /// <summary>更新包的实际目录（输出路径 + 实际使用的更新日期目录名）；未成功时为 <see langword="null"/>。</summary>
    public string? PackagePath { get; }

    /// <summary>「更新前」文件夹里的文件数（成功时有效）。</summary>
    public int BeforeFileCount { get; }

    /// <summary>「更新后」文件夹里的文件数（成功时有效）。</summary>
    public int AfterFileCount { get; }

    /// <summary>变更范围内新增的文件数（成功时有效，供「更新包结构」展示）。</summary>
    public int AddedFileCount { get; }

    /// <summary>变更范围内删除的文件数（成功时有效）。</summary>
    public int DeletedFileCount { get; }

    /// <summary>写出的总字节数（两个快照文件夹 + 更新说明）；成功时有效。</summary>
    public long TotalBytes { get; }

    /// <summary>文件系统冲突清单（<see cref="ExportOutcome.ConflictBlocked"/> 时非空）。</summary>
    public IReadOnlyList<FileSystemConflict>? Conflicts { get; }

    /// <summary>失败类别；成功、空范围与冲突中止时为 <see cref="ExportFailure.None"/>。</summary>
    public ExportFailure Failure { get; }

    /// <summary>给用户看的中文提示（失败原因、为何拒绝导出）；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>面向日志的技术细节（Git 的 stderr 摘要等）。</summary>
    public string? TechnicalDetail { get; }

    /// <summary>构造成功的结果。</summary>
    public static ExportResult Completed(
        string packagePath,
        int beforeFileCount,
        int afterFileCount,
        int addedFileCount,
        int deletedFileCount,
        long totalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        return new(
            ExportOutcome.Completed,
            packagePath,
            beforeFileCount,
            afterFileCount,
            addedFileCount,
            deletedFileCount,
            totalBytes,
            conflicts: null,
            ExportFailure.None,
            failureMessage: null,
            technicalDetail: null);
    }

    /// <summary>构造「空变更范围，拒绝导出」的结果（不创建任何目录）。</summary>
    public static ExportResult EmptyRange()
        => new(
            ExportOutcome.EmptyRange,
            packagePath: null,
            0,
            0,
            0,
            0,
            0,
            conflicts: null,
            ExportFailure.None,
            "变更范围内没有任何文件变更，没有可导出的内容；请回到第 2 步重新选择基准提交与 Head 提交。",
            technicalDetail: null);

    /// <summary>构造「文件系统冲突，已中止导出」的结果（不创建任何目录）。</summary>
    /// <param name="conflicts">完整的冲突清单（非空）。</param>
    public static ExportResult Blocked(IReadOnlyList<FileSystemConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (conflicts.Count == 0)
        {
            throw new ArgumentException("冲突清单不能为空。", nameof(conflicts));
        }

        return new(
            ExportOutcome.ConflictBlocked,
            packagePath: null,
            0,
            0,
            0,
            0,
            0,
            conflicts,
            ExportFailure.None,
            $"导出前检查发现 {conflicts.Count} 处文件系统冲突，已中止导出：请先在仓库里处理下列问题，再重新导出。",
            technicalDetail: null);
    }

    /// <summary>构造失败的结果。</summary>
    public static ExportResult Failed(
        ExportFailure failure,
        string failureMessage,
        string? technicalDetail = null)
    {
        if (failure == ExportFailure.None)
        {
            throw new ArgumentException("失败类别不能是 None。", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(
            ExportOutcome.Failed,
            packagePath: null,
            0,
            0,
            0,
            0,
            0,
            conflicts: null,
            failure,
            failureMessage,
            technicalDetail);
    }
}
