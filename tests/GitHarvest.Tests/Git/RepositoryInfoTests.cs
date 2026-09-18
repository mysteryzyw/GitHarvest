using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 仓库概要的领域规则：提交数是否足以构成「基准 + Head」的变更范围
/// （双点 base..head 要求基准是 Head 的祖先且两者不同，至少要 2 个提交）。
/// </summary>
public sealed class RepositoryInfoTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(7, true)]
    public void 提交数达到两个才足以构成变更范围(int commitCount, bool expected)
    {
        var repository = new RepositoryInfo(@"D:\repo", IsBare: false, CurrentBranch: "main", CommitCount: commitCount);

        Assert.Equal(expected, repository.HasEnoughCommits);
    }
}
