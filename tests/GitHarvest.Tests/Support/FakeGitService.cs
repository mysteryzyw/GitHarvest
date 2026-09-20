using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Support;

/// <summary>
/// <see cref="IGitService"/> 的手写桩：返回预置结论并记录调用，供 Core 的编排类单元测试
/// 断言「门控与顺序」（不跑真实 git——真实 git 的正确性由 Git/ 目录下的集成测试覆盖）。
/// 未预置的能力一律抛 <see cref="NotSupportedException"/>，
/// 让「编排调了不该调的接口」当场暴露。
/// </summary>
internal sealed class FakeGitService : IGitService
{
    /// <summary>祖先校验的结论（预置为真/假即视为校验成功）；不预置且无失败注入时会抛空引用。</summary>
    public bool? IsAncestor { get; init; }

    /// <summary>祖先校验的失败注入。</summary>
    public (CommitListFailure Failure, string Message)? CheckFailure { get; init; }

    /// <summary>范围计算的失败注入。</summary>
    public (CommitListFailure Failure, string Message)? RangeFailure { get; init; }

    /// <summary>范围计算返回的文件清单。</summary>
    public IReadOnlyList<ChangedFile> Files { get; init; } = [];

    /// <summary>blob 哈希 → 内容；导出编排按它写出快照（缺键即按「对象不存在」失败）。</summary>
    public IReadOnlyDictionary<string, byte[]> Blobs { get; init; } =
        new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>取 blob 内容的失败注入（优先于 <see cref="Blobs"/>）。</summary>
    public (CommitListFailure Failure, string Message)? BlobFailure { get; init; }

    /// <summary>被请求过的 blob 哈希（按调用顺序），供断言「只取了该取的内容」。</summary>
    public List<string> RequestedBlobHashes { get; } = [];

    /// <summary>最近一次被请求的范围基准哈希。</summary>
    public string? RequestedBaseHash { get; private set; }

    /// <summary>最近一次被请求的范围 Head 哈希。</summary>
    public string? RequestedHeadHash { get; private set; }

    /// <summary>是否真的起过范围计算。</summary>
    public bool RangeRequested { get; private set; }

    public Task<RepositoryOpenResult> OpenRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用打开仓库。");

    public Task<BranchListResult> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用分支读取。");

    public Task<FetchResult> FetchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用拉取。");

    public Task<CommitListResult> GetCommitsAsync(
        string repositoryPath,
        string reference,
        CommitQuery query,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用提交列表。");

    public Task<CommitDetailResult> GetCommitDetailAsync(
        string repositoryPath,
        string hash,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用提交详情。");

    public Task<AncestorCheckResult> CheckAncestorAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default)
    {
        RequestedBaseHash = baseHash;
        RequestedHeadHash = headHash;

        return Task.FromResult(CheckFailure is { } failure
            ? AncestorCheckResult.Failed(failure.Failure, failure.Message)
            : AncestorCheckResult.Succeeded(IsAncestor!.Value));
    }

    public Task<ChangeRangeResult> GetChangeRangeAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default)
    {
        RangeRequested = true;
        RequestedBaseHash = baseHash;
        RequestedHeadHash = headHash;

        return Task.FromResult(RangeFailure is { } failure
            ? ChangeRangeResult.Failed(failure.Failure, failure.Message)
            : ChangeRangeResult.Succeeded(Files));
    }

    public Task<CommitCountResult> GetRangeCommitCountAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("导出编排不应调用范围提交计数。");

    public Task<BlobCopyResult> CopyBlobToAsync(
        string repositoryPath,
        string blobHash,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        RequestedBlobHashes.Add(blobHash);

        if (BlobFailure is { } failure)
        {
            return Task.FromResult(BlobCopyResult.Failed(failure.Failure, failure.Message));
        }

        if (!Blobs.TryGetValue(blobHash, out var content))
        {
            return Task.FromResult(BlobCopyResult.Failed(
                CommitListFailure.UnknownReference,
                $"仓库里找不到文件内容对象 {blobHash}。"));
        }

        destination.Write(content, 0, content.Length);
        return Task.FromResult(BlobCopyResult.Succeeded());
    }
}
