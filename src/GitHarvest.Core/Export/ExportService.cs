using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// <see cref="IExportService"/> 的实现：导出编排的第一步「导出前总预览」——
/// 祖先校验 → 双点差异 → 按变更类型汇总。git 访问全部经 <see cref="IGitService"/>
/// （ADR-0001：本类不碰进程），因此单元测试可以 mock 该接口覆盖编排与门控。
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly IGitService _gitService;

    /// <param name="gitService">仓库级 Git 操作的唯一接口（祖先校验与双点差异都经它）。</param>
    public ExportService(IGitService gitService)
    {
        ArgumentNullException.ThrowIfNull(gitService);

        _gitService = gitService;
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
}
