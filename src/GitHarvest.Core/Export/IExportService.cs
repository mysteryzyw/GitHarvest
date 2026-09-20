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
}
