using GitHarvest.Core.Export;
using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 导出前总预览编排的单元测试（mock IGitService，不跑 git）：
/// 编排顺序的门控语义——祖先校验不通过时不继续算差异、git 失败透传、
/// 空范围是成功结论、汇总数字与 git 返回的归类一致。
/// 真实 git 产出的正确性见 GetChangeRangeTests（集成）。
/// </summary>
public sealed class ExportServiceTests
{
    [Fact]
    public async Task 祖先校验通过时返回按类型归类的汇总()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            Files =
            [
                Make(ChangeKind.Added, "a1"),
                Make(ChangeKind.Added, "a2"),
                Make(ChangeKind.Deleted, "d"),
                Make(ChangeKind.Modified, "m"),
                Make(ChangeKind.Renamed, "r"),
                Make(ChangeKind.Other, "o"),
            ],
        };

        var result = await CreateService(git).BuildPreviewAsync("repo", "base", "head");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsAncestryViolated);
        Assert.NotNull(result.Summary);
        Assert.Equal(6, result.Summary!.TotalCount);
        Assert.Equal(2, result.Summary.CountOf(ChangeKind.Added));
        Assert.Equal(1, result.Summary.CountOf(ChangeKind.Deleted));
        Assert.Equal(1, result.Summary.CountOf(ChangeKind.Modified));
        Assert.Equal(1, result.Summary.CountOf(ChangeKind.Renamed));
        Assert.Equal(1, result.Summary.CountOf(ChangeKind.Other));
        Assert.Equal("base", git.RequestedBaseHash);
        Assert.Equal("head", git.RequestedHeadHash);
    }

    [Fact]
    public async Task 分叉提交对被禁止且不计算差异()
    {
        var git = new FakeGitService { IsAncestor = false };

        var result = await CreateService(git).BuildPreviewAsync("repo", "base", "head");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsAncestryViolated);
        Assert.Null(result.Summary);
        Assert.NotNull(result.FailureMessage);
        Assert.False(git.RangeRequested); // 祖先校验都没过，不该再起一次 git 进程算差异
    }

    [Fact]
    public async Task 祖先校验失败时透传失败类别与提示()
    {
        var git = new FakeGitService
        {
            CheckFailure = (CommitListFailure.GitUnavailable, "Git 尚未就绪。"),
        };

        var result = await CreateService(git).BuildPreviewAsync("repo", "base", "head");

        Assert.False(result.IsSuccess);
        Assert.False(result.IsAncestryViolated);
        Assert.Equal(CommitListFailure.GitUnavailable, result.Failure);
        Assert.Equal("Git 尚未就绪。", result.FailureMessage);
    }

    [Fact]
    public async Task 差异计算失败时透传失败类别与提示()
    {
        var git = new FakeGitService
        {
            IsAncestor = true,
            RangeFailure = (CommitListFailure.UnknownReference, "找不到提交「base..head」。"),
        };

        var result = await CreateService(git).BuildPreviewAsync("repo", "base", "head");

        Assert.False(result.IsSuccess);
        Assert.False(result.IsAncestryViolated);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
        Assert.Equal("找不到提交「base..head」。", result.FailureMessage);
    }

    [Fact]
    public async Task 空变更范围是成功结论且汇总为空()
    {
        var git = new FakeGitService { IsAncestor = true, Files = [] };

        var result = await CreateService(git).BuildPreviewAsync("repo", "base", "head");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.IsEmpty);
        Assert.Equal(0, result.Summary.TotalCount);
    }

    private static ExportService CreateService(FakeGitService git) => new(git);

    private static ChangedFile Make(ChangeKind kind, string path)
        => new(path, null, kind, OtherChangeReason.None, 1, 1, null, 10, null, null);

    /// <summary>IGitService 的手写桩：返回预置结论并记录调用，供编排测试断言门控。</summary>
    private sealed class FakeGitService : IGitService
    {
        public bool? IsAncestor { get; init; }

        public (CommitListFailure Failure, string Message)? CheckFailure { get; init; }

        public (CommitListFailure Failure, string Message)? RangeFailure { get; init; }

        public IReadOnlyList<ChangedFile> Files { get; init; } = [];

        public string? RequestedBaseHash { get; private set; }

        public string? RequestedHeadHash { get; private set; }

        public bool RangeRequested { get; private set; }

        public Task<RepositoryOpenResult> OpenRepositoryAsync(string repositoryPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用打开仓库。");

        public Task<BranchListResult> GetBranchesAsync(string repositoryPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用分支读取。");

        public Task<FetchResult> FetchAsync(string repositoryPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用拉取。");

        public Task<CommitListResult> GetCommitsAsync(string repositoryPath, string reference, CommitQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用提交列表。");

        public Task<CommitDetailResult> GetCommitDetailAsync(string repositoryPath, string hash, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用提交详情。");

        public Task<AncestorCheckResult> CheckAncestorAsync(string repositoryPath, string baseHash, string headHash, CancellationToken cancellationToken = default)
        {
            RequestedBaseHash = baseHash;
            RequestedHeadHash = headHash;
            return Task.FromResult(CheckFailure is { } failure
                ? AncestorCheckResult.Failed(failure.Failure, failure.Message)
                : AncestorCheckResult.Succeeded(IsAncestor!.Value));
        }

        public Task<ChangeRangeResult> GetChangeRangeAsync(string repositoryPath, string baseHash, string headHash, CancellationToken cancellationToken = default)
        {
            RangeRequested = true;
            return Task.FromResult(RangeFailure is { } failure
                ? ChangeRangeResult.Failed(failure.Failure, failure.Message)
                : ChangeRangeResult.Succeeded(Files));
        }

        public Task<CommitCountResult> GetRangeCommitCountAsync(string repositoryPath, string baseHash, string headHash, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("预览编排不应调用范围提交计数。");
    }
}
