using GitHarvest.Core.Git;

namespace GitHarvest.Core.Interaction;

/// <summary>
/// 第 2 步「选择提交」选定的变更范围：基准提交与 Head 提交的摘要。
/// 复用 <see cref="CommitSummary"/>（哈希、信息首行、作者、时间）——第 3 步的预览标题、
/// 第 4 步更新说明的占位符（基准哈希 / 基准信息等）都来自同一份数据，避免各页各存一份。
/// 基准与 Head 都选定后由选择提交页整体写入；任一侧被取消选择时整体清空。
/// </summary>
/// <param name="Base">基准提交（变更范围的起点，本身变更不导出）。</param>
/// <param name="Head">Head 提交（变更范围的终点，本身变更包含在导出内）。</param>
public sealed record RangeSelection(CommitSummary Base, CommitSummary Head)
{
    /// <summary>双点记号的短哈希形态（「a1b2c3d..e4f5g6h」），预览页副标题直接使用。</summary>
    public string RangeText => $"{Base.ShortHash}..{Head.ShortHash}";
}
