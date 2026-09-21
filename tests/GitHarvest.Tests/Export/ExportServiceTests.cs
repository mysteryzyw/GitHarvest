using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

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

    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private static ExportService CreateService(FakeGitService git)
        => new(
            git,
            new GitHarvest.Core.Templates.TemplateService(new StubSettingsService(), SilentLogger),
            new RecordingHistoryService(),
            SilentLogger);

    private static ChangedFile Make(ChangeKind kind, string path)
        => new(path, null, kind, OtherChangeReason.None, 1, 1, null, 10, null, null);
}
