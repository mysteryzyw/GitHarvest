using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// 一次导出更新包的请求：范围（基准 / Head）、输出落点与更新说明要用的元信息。
/// 时间与目录名都由调用方给出——编排层不读系统时钟，这样「同名目录让位成 _HHmmss」
/// 与「说明里的更新日期」都可测，且与界面显示的值必然一致。
/// </summary>
/// <param name="RepositoryPath">仓库目录（仓库根或其子目录）。</param>
/// <param name="OutputRootPath">输出路径（不含更新日期目录，更新包落在它下面的 <see cref="FolderName"/> 里）。</param>
/// <param name="FolderName">更新日期目录名（默认 <c>yyyy-MM-dd</c>，用户可编辑；已存在时由编排层让位）。</param>
/// <param name="RequestedAt">发起导出的时间点（生成同名目录后缀、说明里的日期都用它）。</param>
/// <param name="BranchName">导出时所在的分支名（游离头指针时为 <c>HEAD</c>），写进更新说明。</param>
/// <param name="Base">基准提交摘要（哈希与信息写进更新说明）。</param>
/// <param name="Head">Head 提交摘要（哈希与信息写进更新说明）。</param>
public sealed record ExportRequest(
    string RepositoryPath,
    string OutputRootPath,
    string FolderName,
    DateTimeOffset RequestedAt,
    string BranchName,
    CommitSummary Base,
    CommitSummary Head);
