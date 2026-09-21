using System.Text.Json.Serialization;

namespace GitHarvest.Core.History;

/// <summary>
/// 导出历史里的一条记录（CONTEXT.md 的「导出历史」）：一次成功导出的轻量留痕——
/// 时间、仓库、变更范围（基准与 Head 提交）与输出路径，用于事后追溯
/// 「什么时候给哪个客户导出了哪一版」（spec 用户故事 45）。
/// 刻意不记录更新包内容（文件清单、说明文本）：那些随更新包一起交付，导出历史只做索引。
/// </summary>
/// <param name="ExportedAt">这次导出发起的时间点。</param>
/// <param name="RepositoryPath">被导出仓库的根路径。</param>
/// <param name="BranchName">导出时所在的分支名（游离头指针时为 <c>HEAD</c>）。</param>
/// <param name="BaseHash">基准提交的短哈希。</param>
/// <param name="HeadHash">Head 提交的短哈希。</param>
/// <param name="OutputPath">更新包实际落地的目录（含重名让位后的更新日期目录名）。</param>
public sealed record ExportHistoryEntry(
    [property: JsonPropertyName("exportedAt")] DateTimeOffset ExportedAt,
    [property: JsonPropertyName("repositoryPath")] string RepositoryPath,
    [property: JsonPropertyName("branchName")] string BranchName,
    [property: JsonPropertyName("baseHash")] string BaseHash,
    [property: JsonPropertyName("headHash")] string HeadHash,
    [property: JsonPropertyName("outputPath")] string OutputPath);
