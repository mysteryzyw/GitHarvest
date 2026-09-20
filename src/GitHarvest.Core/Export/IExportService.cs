using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// 导出编排（spec 的 Core 服务划分：范围计算 → 文件系统冲突预扫描 → 快照写出 →
/// 更新说明生成 → 导出历史追加）。本接口随 ticket 08 从「导出前总预览」起步，
/// 快照写出等能力在 ticket 09 扩展——与 IGitService 一样在同一接口上演进，勿另立。
/// ViewModel 只依赖本接口，不接触 git 进程与文件系统。
/// </summary>
public interface IExportService
{
    /// <summary>
    /// 导出前总预览（导出编排的第一步）：先做祖先校验（基准必须是 Head 的祖先，
    /// 分叉提交对禁止导出），通过后计算双点变更范围并汇总成按变更类型分组的统计。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="baseHash">基准提交（完整 / 短哈希）。</param>
    /// <param name="headHash">Head 提交（完整 / 短哈希）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>预览结果；空变更范围是成功结论（<see cref="ChangeRangeSummary.IsEmpty"/>），禁止导出的判断由界面做。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<ChangeRangePreviewResult> BuildPreviewAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出前的预检（不写出任何文件）：解析更新日期目录名（重名即让位）、计算变更范围、
    /// 规划快照落位并扫描文件系统冲突，一次把界面需要的信息都算出来。
    /// 界面在用户按下导出前就能展示「更新包将包含什么」「实际会落到哪个目录」与冲突清单
    /// （用户故事 31 / 32）；真正导出时 <see cref="ExportAsync"/> 会再走一遍同样的检查作为兜底。
    /// </summary>
    /// <param name="request">导出请求（范围、输出落点、更新说明的元信息）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>预检结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<ExportPrecheckResult> InspectAsync(
        ExportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出更新包（编排的第二到第五步）：范围计算 → 文件系统冲突预扫描 → 快照写出 →
    /// 更新说明生成。产出位于 <c>输出路径/&lt;更新日期&gt;/</c>，含「更新前」「更新后」
    /// 两个快照文件夹与「更新说明.md」。
    /// 四种结论互斥：完成、空范围拒绝（用户故事 30）、文件系统冲突中止（用户故事 32，清单随结果返回）、
    /// 失败。**取消与失败都会清理半成品目录**——绝不留下一个看起来像交付物的残包。
    /// 更新日期目录名已存在时自动让位为 <c>_HHmmss</c>（必要时再追加序号），
    /// 实际使用的目录名以 <see cref="ExportResult.PackagePath"/> 为准（用户故事 31）。
    /// </summary>
    /// <param name="request">导出请求（范围、输出落点、更新说明的元信息）。</param>
    /// <param name="progress">进度回调；不需要进度时传 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程并清理半成品。</param>
    /// <returns>本次导出的结论（<see cref="ExportResult"/>）；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消（半成品已清理）。</exception>
    Task<ExportResult> ExportAsync(
        ExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
