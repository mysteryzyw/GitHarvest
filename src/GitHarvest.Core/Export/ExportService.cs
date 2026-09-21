using GitHarvest.Core.Git;
using GitHarvest.Core.History;
using GitHarvest.Core.Templates;
using Serilog;

namespace GitHarvest.Core.Export;

/// <summary>
/// <see cref="IExportService"/> 的实现：导出编排的两步能力——
/// 「导出前总预览」（祖先校验 → 双点差异 → 按变更类型汇总）与
/// 「导出更新包」（范围 → 冲突预扫描 → 快照写出 → 更新说明 → 导出历史）。
/// git 访问全部经 <see cref="IGitService"/>（ADR-0001：本类不碰 git 进程；文件系统则归本类，
/// 这是「快照写出」这一步的职责），因此单元测试可以 mock 该接口、只留真实临时目录来断言产物。
/// 更新说明的文本由 <see cref="ITemplateService"/> 渲染（模板或页面编辑后的本次内容）；
/// 导出历史经 <see cref="IHistoryService"/> 追加。
/// </summary>
public sealed class ExportService : IExportService
{
    /// <summary>更新说明的编码：UTF-8 且不带 BOM（spec 用户故事 41：跨工具兼容）。</summary>
    private static readonly System.Text.UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IGitService _gitService;
    private readonly ITemplateService _templateService;
    private readonly IHistoryService _historyService;
    private readonly ILogger _logger;

    /// <param name="gitService">仓库级 Git 操作的唯一接口（范围计算与取文件内容都经它）。</param>
    /// <param name="templateService">更新说明的模板加载与占位符渲染。</param>
    /// <param name="historyService">导出历史的追加（编排的最后一步：成功才记）。</param>
    /// <param name="logger">导出过程与失败原因写这里，便于排查。</param>
    public ExportService(
        IGitService gitService,
        ITemplateService templateService,
        IHistoryService historyService,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(templateService);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(logger);

        _gitService = gitService;
        _templateService = templateService;
        _historyService = historyService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChangeRangePreviewResult> BuildPreviewAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(headHash);

        // 第一步：祖先校验。校验流程失败直接透出；「不是祖先」（分叉提交对）是
        // 确定的结论——禁止导出，不再继续算差异。
        var check = await _gitService.CheckAncestorAsync(repositoryPath, baseHash, headHash, cancellationToken)
            .ConfigureAwait(false);
        if (!check.IsSuccess)
        {
            return ChangeRangePreviewResult.Failed(check.Failure, check.FailureMessage!, check.TechnicalDetail);
        }

        if (!check.IsAncestor)
        {
            return ChangeRangePreviewResult.AncestryViolated();
        }

        // 第二步：双点差异 + 汇总。空差异是成功结论（空汇总），禁止导出的判断由界面呈现。
        var range = await _gitService.GetChangeRangeAsync(repositoryPath, baseHash, headHash, cancellationToken)
            .ConfigureAwait(false);
        if (!range.IsSuccess)
        {
            return ChangeRangePreviewResult.Failed(range.Failure, range.FailureMessage!, range.TechnicalDetail);
        }

        return ChangeRangePreviewResult.Succeeded(new ChangeRangeSummary(range.Files!));
    }

    /// <inheritdoc />
    public async Task<ExportPrecheckResult> InspectAsync(
        ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inspection = await InspectCoreAsync(request, progress: null, cancellationToken).ConfigureAwait(false);
        return inspection.Precheck;
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 第二、三步：预检（请求校验 → 目录名解析 → 范围计算 → 落位与冲突扫描）。
        // 导出不信任页面上的预检结论，这里重走一遍——它是唯一的真相。
        var inspection = await InspectCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        var precheck = inspection.Precheck;

        if (precheck.IsFailed)
        {
            return ExportResult.Failed(precheck.Failure, precheck.FailureMessage!, precheck.TechnicalDetail);
        }

        if (precheck.IsEmptyRange)
        {
            // 空范围在这里就结束：一个文件都不写，连目录都不建（用户故事 30）。
            _logger.Information("导出被拒绝：变更范围内没有任何文件变更。{RepositoryPath}", request.RepositoryPath);
            return ExportResult.EmptyRange();
        }

        if (precheck.IsBlockedByConflicts)
        {
            _logger.Warning(
                "导出被中止：发现 {ConflictCount} 处文件系统冲突。{RepositoryPath}",
                precheck.Conflicts!.Count,
                request.RepositoryPath);
            return ExportResult.Blocked(precheck.Conflicts);
        }

        var plan = inspection.Plan!;
        var summary = inspection.Summary!;
        var packagePath = precheck.Plan!.PackagePath;

        // 第四、五步：写出快照与更新说明。取消或失败都清理半成品目录。
        try
        {
            var result = await WritePackageAsync(
                request,
                packagePath,
                plan,
                summary,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                // 写内容中途失败（如某个 blob 取不到）：按「不留半成品」处理。
                Cleanup(packagePath);
                return result;
            }

            _logger.Information(
                "更新包导出完成：{PackagePath}（更新前 {BeforeCount} 个文件、更新后 {AfterCount} 个文件、共 {TotalBytes} 字节）",
                packagePath,
                result.BeforeFileCount,
                result.AfterFileCount,
                result.TotalBytes);

            AppendHistory(request, packagePath);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Information("导出被取消，正在清理半成品：{PackagePath}", packagePath);
            Cleanup(packagePath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error(exception, "导出失败（文件系统）：{PackagePath}", packagePath);
            Cleanup(packagePath);
            return ExportResult.Failed(
                ExportFailure.FileSystem,
                $"写出更新包失败：{DescribeFileSystemFailure(exception)}。未完成的产物已清理，详情请查看日志。",
                exception.Message);
        }
    }

    /// <summary>
    /// 预检的内部形态：既给出对外的预检结果，也把编排后续要用的汇总与落位计划带出来，
    /// 避免真正导出时重算一遍（预检与导出因此共用同一段编排）。
    /// </summary>
    /// <param name="Precheck">对外的预检结果。</param>
    /// <param name="Summary">变更范围汇总（失败或空范围时为 <see langword="null"/>）。</param>
    /// <param name="Plan">快照落位计划（未算到差异时为 <see langword="null"/>）。</param>
    private sealed record Inspection(ExportPrecheckResult Precheck, ChangeRangeSummary? Summary, SnapshotPlan? Plan);

    /// <summary>
    /// 预检的实现（导出与预检共用）：请求校验 → 目录名解析 → 范围计算 → 空范围判断 →
    /// 落位规划 → 文件系统冲突扫描。任何一步失败都提前返回，不做多余的 git 调用。
    /// </summary>
    /// <param name="request">导出请求。</param>
    /// <param name="progress">进度回调；真正导出时传入（预检时不报进度），为 <see langword="null"/> 表示不报。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Inspection> InspectCoreAsync(
        ExportRequest request,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 第一步：请求校验。界面在导出前也会挡这两条，这里再挡一次是为了守住 Core 的底线——
        // 绝不因为调用方漏判而把包写到意外的位置或用非法的目录名。
        if (string.IsNullOrWhiteSpace(request.OutputRootPath))
        {
            return Failed(ExportPrecheckResult.Failed(ExportFailure.InvalidRequest, "请先选择输出路径，再导出更新包。"));
        }

        if (ExportFolderName.Validate(request.FolderName) is { } folderNameError)
        {
            return Failed(ExportPrecheckResult.Failed(ExportFailure.InvalidRequest, folderNameError));
        }

        Report(progress, ExportPhase.Preparing, 0, 0);

        if (ResolvePackagePath(request, out var packagePath) is { } pathFailure)
        {
            return Failed(pathFailure);
        }

        // 第二步：变更范围（与预览页同一入口）。
        Report(progress, ExportPhase.ComputingRange, 0, 0);
        var preview = await BuildPreviewAsync(
            request.RepositoryPath,
            request.Base.ShortHash,
            request.Head.ShortHash,
            cancellationToken).ConfigureAwait(false);

        if (!preview.IsSuccess)
        {
            return Failed(ExportPrecheckResult.Failed(
                ExportFailure.GitAccess,
                preview.FailureMessage!,
                preview.TechnicalDetail));
        }

        var summary = preview.Summary!;
        if (summary.IsEmpty)
        {
            return new Inspection(ExportPrecheckResult.EmptyRange(), Summary: null, Plan: null);
        }

        // 第三步：落位 + 冲突预扫描。扫描用的路径与写出用的是同一份计算（UpdatePackageLayout），
        // 因此「扫过的」就是「要写的」。
        Report(progress, ExportPhase.ScanningConflicts, 0, 0);
        var plan = SnapshotPlanner.Plan(summary.Files);
        var conflicts = FileSystemConflictScanner.Scan(plan, packagePath);

        var packagePlan = new UpdatePackagePlan(
            packagePath,
            Path.GetFileName(packagePath),
            plan.BeforeCount,
            plan.AfterCount,
            summary.CountOf(ChangeKind.Added),
            summary.CountOf(ChangeKind.Deleted));

        return conflicts.Count > 0
            ? new Inspection(ExportPrecheckResult.Blocked(packagePlan, conflicts, summary), summary, plan)
            : new Inspection(ExportPrecheckResult.Ready(packagePlan, summary), summary, plan);

        Inspection Failed(ExportPrecheckResult precheck) => new(precheck, Summary: null, Plan: null);
    }

    /// <summary>
    /// 解析更新包的最终目录：输出路径 + 更新日期目录名（已存在则让位成 <c>_HHmmss</c>）。
    /// 只做路径计算，不创建目录——空范围与冲突中止都不该在输出路径留下空目录。
    /// </summary>
    private ExportPrecheckResult? ResolvePackagePath(ExportRequest request, out string packagePath)
    {
        packagePath = string.Empty;

        string outputRoot;
        try
        {
            outputRoot = Path.GetFullPath(request.OutputRootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ExportPrecheckResult.Failed(
                ExportFailure.InvalidRequest,
                $"输出路径无效：{request.OutputRootPath}",
                exception.Message);
        }

        var folderName = ExportFolderName.ResolveUnique(
            request.FolderName,
            request.RequestedAt,
            name => Directory.Exists(Path.Combine(outputRoot, name)));

        packagePath = Path.Combine(outputRoot, folderName);

        if (!string.Equals(folderName, request.FolderName, StringComparison.Ordinal))
        {
            _logger.Information(
                "更新日期目录名已被占用，本次实际使用：{ActualFolderName}（请求 {RequestedFolderName}）",
                folderName,
                request.FolderName);
        }

        return null;
    }

    /// <summary>
    /// 写出更新包：先建好固定结构（两个快照文件夹），再逐个把 blob 内容写进对应路径，
    /// 最后写更新说明。进度按「一步 = 一个快照文件」推进，更新说明算最后一步。
    /// </summary>
    private async Task<ExportResult> WritePackageAsync(
        ExportRequest request,
        string packagePath,
        SnapshotPlan plan,
        ChangeRangeSummary summary,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalSteps = plan.Entries.Count + 1;
        var completedSteps = 0;
        var totalBytes = 0L;

        Directory.CreateDirectory(packagePath);

        // 两个文件夹固定存在（CONTEXT.md 的更新包结构）：只有新增时「更新前」是空文件夹，
        // 只有删除时「更新后」是空文件夹——结构固定，现场一眼能看出哪一侧没有内容。
        Directory.CreateDirectory(UpdatePackageLayout.FolderPath(packagePath, SnapshotSide.Before));
        Directory.CreateDirectory(UpdatePackageLayout.FolderPath(packagePath, SnapshotSide.After));

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = UpdatePackageLayout.FullPath(packagePath, entry.Side, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            long writtenBytes;
            await using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var copy = await _gitService
                    .CopyBlobToAsync(request.RepositoryPath, entry.BlobHash, destination, cancellationToken)
                    .ConfigureAwait(false);

                if (!copy.IsSuccess)
                {
                    return ExportResult.Failed(ExportFailure.GitAccess, copy.FailureMessage!, copy.TechnicalDetail);
                }

                writtenBytes = destination.Length;
            }

            totalBytes += writtenBytes;
            Report(progress, ExportPhase.WritingSnapshots, ++completedSteps, totalSteps);
        }

        Report(progress, ExportPhase.WritingNotes, completedSteps, totalSteps);

        var notes = await ComposeNotesAsync(request, summary, cancellationToken).ConfigureAwait(false);
        var notesBytes = Utf8WithoutBom.GetBytes(notes);
        await File.WriteAllBytesAsync(UpdatePackageLayout.NotesPath(packagePath), notesBytes, cancellationToken)
            .ConfigureAwait(false);

        totalBytes += notesBytes.Length;
        Report(progress, ExportPhase.WritingNotes, ++completedSteps, totalSteps);

        return ExportResult.Completed(
            packagePath,
            plan.BeforeCount,
            plan.AfterCount,
            summary.CountOf(ChangeKind.Added),
            summary.CountOf(ChangeKind.Deleted),
            totalBytes);
    }

    /// <summary>
    /// 生成要随更新包写出的更新说明文本（LF 换行）：
    /// 页面给了本次编辑内容（<see cref="ExportRequest.NotesOverride"/>）就以它为准，
    /// 否则按当前生效的模板（自定义或内置）生成；两者都过同一渲染入口——
    /// 编辑时插入的占位符在写出前同样被渲染，未知占位符原样保留并记 Warning（用户故事 39/40）。
    /// </summary>
    private async Task<string> ComposeNotesAsync(
        ExportRequest request,
        ChangeRangeSummary summary,
        CancellationToken cancellationToken)
    {
        var context = NotesTemplateContext.From(request, summary);

        RenderedNotes rendered;
        if (request.NotesOverride is { } overrideText)
        {
            rendered = _templateService.Render(overrideText, context);
        }
        else
        {
            var loaded = await _templateService.LoadTemplateAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Notice is { } notice)
            {
                _logger.Warning("生成更新说明：{Notice}", notice);
            }

            rendered = _templateService.Render(loaded.Text, context);
        }

        foreach (var warning in rendered.Warnings)
        {
            _logger.Warning("更新说明：{PlaceholderWarning}", warning);
        }

        // 编辑器给出的是 Windows 换行：统一成 LF，跨工具打开不窜行（与 UTF-8 无 BOM 同为兼容性约定）。
        return rendered.Content.Replace("\r\n", "\n");
    }

    /// <summary>
    /// 追加导出历史（编排的最后一步，spec 用户故事 45）：只有成功产出更新包才记一条——
    /// 空范围、冲突中止、失败与取消都不产生记录，历史里出现的每个更新包都是真实存在的产物。
    /// 时间取请求里的发起时刻（Core 不读系统时钟），与更新日期目录名、更新说明里的日期同源；
    /// 输出路径记实际落地的目录（含重名让位后的名字）。
    /// 写入失败由 <see cref="IHistoryService"/> 自己降级为 Warning，不改变本次导出的结论。
    /// </summary>
    private void AppendHistory(ExportRequest request, string packagePath)
        => _historyService.Append(new ExportHistoryEntry(
            request.RequestedAt,
            request.RepositoryPath,
            request.BranchName,
            request.Base.ShortHash,
            request.Head.ShortHash,
            packagePath));

    /// <summary>
    /// 递归删除未完成的更新包目录。删除失败只记 Warning：原始失败（或取消）才是要给用户的结论，
    /// 不能被清理噪声盖掉。
    /// </summary>
    private void Cleanup(string packagePath)
    {
        try
        {
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "清理未完成的更新包目录失败：{PackagePath}", packagePath);
        }
    }

    /// <summary>把文件系统异常翻成一句人能读的原因（磁盘满、无权限、路径过长是最常见的三种）。</summary>
    private static string DescribeFileSystemFailure(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "没有写入权限，或目标被其他程序占用",
        PathTooLongException => "路径过长，超出 Windows 的限制",
        DirectoryNotFoundException => "输出路径不存在或已被移动",
        _ => "磁盘空间不足或路径不可写",
    };

    /// <summary>上报一次进度（没有进度回调时什么都不做）。</summary>
    private static void Report(IProgress<ExportProgress>? progress, ExportPhase phase, int completedSteps, int totalSteps)
        => progress?.Report(new ExportProgress(phase, completedSteps, totalSteps));
}
