using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 祖先校验与双点变更范围计算的集成测试：跑真实 git.exe，断言外部行为——
/// base..head 的语义（含 Head、不含基准）、五类变更的归类（重命名按 git -M 默认
/// 50% 阈值、子模块指针的新增 / 删除 / 换目标三种形态都在真实仓库里得到验证）、
/// +行数 / 大小 / 新旧 blob、分叉提交对被识别、中文文件名与二进制文件不受编码影响。
/// 期望值用独立 git 命令交叉验证，不硬编码哈希。
/// </summary>
public sealed class GetChangeRangeTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    // ============ GetChangeRangeAsync：范围语义与归类 ============

    [Fact]
    public async Task 基准自身的变更不进入范围而Head的变更进入()
    {
        var repositoryPath = await CreateRepositoryAsync("range-semantic");
        // c1 加 a.txt；c2（=基准）加 base-only.txt；c3（=Head）加 head-only.txt。
        await CommitFileAsync(repositoryPath, "a.txt", "a", "c1");
        await CommitFileAsync(repositoryPath, "base-only.txt", "b", "c2");
        await CommitFileAsync(repositoryPath, "head-only.txt", "c", "c3");
        var (baseHash, headHash) = await ResolveRangeAsync(repositoryPath, "HEAD~1", "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var file = Assert.Single(result.Files!);
        Assert.Equal("head-only.txt", file.Path);
        Assert.Equal(ChangeKind.Added, file.Kind);
    }

    [Fact]
    public async Task 增删改重命名四类变更归类与数字正确()
    {
        var repositoryPath = await CreateRepositoryAsync("kinds");
        await CommitFileAsync(repositoryPath, "del.txt", "要删除的文件", "c1");
        await CommitFileAsync(
            repositoryPath,
            "renamed.txt",
            "line 01\nline 02\nline 03\nline 04\nline 05\nline 06\nline 07\nline 08\nline 09\nline 10\n",
            "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await CommitFileAsync(repositoryPath, "added.txt", "hello", "c2");
        await TestGit.RunAsync(["rm", "-q", "del.txt"], repositoryPath);
        // 重命名 + 只改一行：相似度约 90%，远高于 git -M 的默认 50% 阈值 → 识别为重命名。
        await TestGit.RunAsync(["mv", "renamed.txt", "moved.txt"], repositoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "moved.txt"),
            "line 01\nline 02\nline 03\nline 04\nline 05\nline 06\nline 07\nline 08\nline 09\nCHANGED\n");
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "c2"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess);
        var files = result.Files!;

        var added = Assert.Single(files, f => f.Path == "added.txt");
        Assert.Equal(ChangeKind.Added, added.Kind);
        Assert.Null(added.OldBlobHash);
        Assert.NotNull(added.NewBlobHash);
        Assert.Equal(1, added.Additions);
        Assert.Equal(0, added.Deletions);
        Assert.Equal(5L, added.SizeBytes); // blob 内容是「hello」5 个字节

        var deleted = Assert.Single(files, f => f.Path == "del.txt");
        Assert.Equal(ChangeKind.Deleted, deleted.Kind);
        Assert.NotNull(deleted.OldBlobHash);
        Assert.Null(deleted.NewBlobHash);
        // 「更新前」版本的大小：blob 内容是「要删除的文件」（6 个汉字 × 3 字节 = 18 字节）。
        Assert.Equal(18L, deleted.SizeBytes);

        var renamed = Assert.Single(files, f => f.Path == "moved.txt");
        Assert.Equal(ChangeKind.Renamed, renamed.Kind);
        Assert.Equal("renamed.txt", renamed.OldPath);
        Assert.NotNull(renamed.SimilarityPercent);
        Assert.True(renamed.SimilarityPercent! >= 50, $"相似度 {renamed.SimilarityPercent} 应 ≥ 50%");
        Assert.Equal(1, renamed.Additions);
        Assert.Equal(1, renamed.Deletions);

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public async Task 重命名且相似度低于50阈值时拆成删除加新增()
    {
        var repositoryPath = await CreateRepositoryAsync("rename-below-threshold");
        await CommitFileAsync(
            repositoryPath,
            "old-name.txt",
            "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\nten\n",
            "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await TestGit.RunAsync(["mv", "old-name.txt", "new-name.txt"], repositoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "new-name.txt"),
            "1st\n2nd\n3rd\n4th\n5th\n6th\n7th\n8th\n9th\n10th\n");
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "c2"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Files!.Count);
        Assert.Equal(ChangeKind.Deleted, Assert.Single(result.Files, f => f.Path == "old-name.txt").Kind);
        Assert.Equal(ChangeKind.Added, Assert.Single(result.Files, f => f.Path == "new-name.txt").Kind);
    }

    [Fact]
    public async Task 合并提交在范围内时按双树差异取并集()
    {
        var repositoryPath = await CreateRepositoryAsync("merge-in-range");
        await CommitFileAsync(repositoryPath, "base.txt", "base", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        await TestGit.RunAsync(["checkout", "-q", "-b", "feature"], repositoryPath);
        await CommitFileAsync(repositoryPath, "feature.txt", "分支内容", "feat");
        await TestGit.RunAsync(["checkout", "-q", "main"], repositoryPath);
        await CommitFileAsync(repositoryPath, "main.txt", "主干内容", "main-fix");
        await TestGit.RunAsync(
            ["merge", "-q", "--no-ff", "-m", "merge", "feature"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess);
        // 范围是 base..合并提交 的树差异：两个分支各自的新文件都在。
        Assert.Equal(
            ["feature.txt", "main.txt"],
            result.Files!.Select(f => f.Path).Order().ToArray());
    }

    [Fact]
    public async Task 二进制文件行数为空但大小与类型正常()
    {
        var repositoryPath = await CreateRepositoryAsync("binary");
        await CommitFileAsync(repositoryPath, "keep.txt", "text", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        // 含 NUL 字节的内容：git 判定为二进制，numstat 以 "-" 占位。
        await File.WriteAllBytesAsync(
            Path.Combine(repositoryPath, "logo.bin"),
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0xFF]);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "c2"], repositoryPath);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess);
        var binary = Assert.Single(result.Files!, f => f.Path == "logo.bin");
        Assert.Equal(ChangeKind.Added, binary.Kind);
        Assert.Null(binary.Additions);
        Assert.Null(binary.Deletions);
        Assert.False(binary.HasLineCounts);
        Assert.Equal(11L, binary.SizeBytes);
    }

    [Fact]
    public async Task 子模块的新增删除与指针变化都归其他()
    {
        var repositoryPath = await CreateRepositoryAsync("submodule-kinds");
        await CommitFileAsync(repositoryPath, "keep.txt", "text", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");

        // 新增子模块（A）：指针指向基准提交；同一提交里放一个普通文件，验证两者被区分归类。
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "normal.txt"), "普通文件");
        await TestGit.RunAsync(["add", "normal.txt"], repositoryPath);
        await CommitGitlinkAsync(repositoryPath, "vendor/lib", baseHash, "新增子模块");
        var addedHash = await ResolveAsync(repositoryPath, "HEAD");

        // 造出第二个可指向的提交（空提交，不动索引），再把指针指过去（M）。
        await TestGit.RunAsync(["commit", "-q", "--allow-empty", "-m", "空提交"], repositoryPath);
        var anotherHash = await ResolveAsync(repositoryPath, "HEAD");
        await CommitGitlinkAsync(repositoryPath, "vendor/lib", anotherHash, "换指针");
        var movedHash = await ResolveAsync(repositoryPath, "HEAD");

        // 删除子模块（D）。
        await TestGit.RunAsync(["rm", "-q", "--cached", "vendor/lib"], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", "删除子模块"], repositoryPath);
        var removedHash = await ResolveAsync(repositoryPath, "HEAD");

        var service = CreateService();

        var addedRange = await service.GetChangeRangeAsync(repositoryPath, baseHash, addedHash);
        Assert.True(addedRange.IsSuccess, addedRange.FailureMessage);
        var addedSubmodule = Assert.Single(addedRange.Files!, f => f.Path == "vendor/lib");
        Assert.Equal(ChangeKind.Other, addedSubmodule.Kind);
        Assert.Equal(OtherChangeReason.SubmodulePointer, addedSubmodule.OtherReason);
        Assert.Equal(ChangeKind.Added, Assert.Single(addedRange.Files!, f => f.Path == "normal.txt").Kind);

        // 指针变化（M）与删除（D）两个范围里，条目同样归「其他·子模块指针」。
        foreach (var (startHash, endHash) in new[] { (anotherHash, movedHash), (movedHash, removedHash) })
        {
            var result = await service.GetChangeRangeAsync(repositoryPath, startHash, endHash);

            Assert.True(result.IsSuccess, result.FailureMessage);
            var submodule = Assert.Single(result.Files!);
            Assert.Equal("vendor/lib", submodule.Path);
            Assert.Equal(ChangeKind.Other, submodule.Kind);
            Assert.Equal(OtherChangeReason.SubmodulePointer, submodule.OtherReason);
            // 子模块没有内容大小（ls-tree 以 "-" 占位）。
            Assert.Null(submodule.SizeBytes);
        }
    }

    [Fact]
    public async Task 中文文件名与中文目录保持原样()
    {
        var repositoryPath = await CreateRepositoryAsync("chinese-paths");
        await CommitFileAsync(repositoryPath, "说明.md", "文本", "c1");
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "模块"));
        await CommitFileAsync(repositoryPath, Path.Combine("模块", "配置.json"), "{}", "c2");
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(repositoryPath, baseHash, headHash);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["模块/配置.json"],
            result.Files!.Select(f => f.Path).ToArray());
    }

    [Fact]
    public async Task 基准与Head为同一提交时祖先成立且范围为空()
    {
        var repositoryPath = await CreateRepositoryAsync("same-commit", commitCount: 2);
        var hash = await ResolveAsync(repositoryPath, "HEAD");

        var check = await CreateService().CheckAncestorAsync(repositoryPath, hash, hash);
        var range = await CreateService().GetChangeRangeAsync(repositoryPath, hash, hash);

        Assert.True(check.IsSuccess);
        Assert.True(check.IsAncestor);
        Assert.True(range.IsSuccess);
        Assert.Empty(range.Files!);
    }

    // ============ CheckAncestorAsync：祖先校验 ============

    [Fact]
    public async Task 同一祖先链上的提交对通过校验()
    {
        var repositoryPath = await CreateRepositoryAsync("ancestor-ok", commitCount: 3);

        var check = await CreateService().CheckAncestorAsync(
            repositoryPath, await ResolveAsync(repositoryPath, "HEAD~2"), await ResolveAsync(repositoryPath, "HEAD"));

        Assert.True(check.IsSuccess);
        Assert.True(check.IsAncestor);
    }

    [Fact]
    public async Task 分叉提交对不是祖先()
    {
        var repositoryPath = await CreateRepositoryAsync("forked", commitCount: 1);
        // 从同一基点分出两条链：两条链的 tip 互不为祖先。
        await TestGit.RunAsync(["checkout", "-q", "-b", "chain-a"], repositoryPath);
        await CommitFileAsync(repositoryPath, "a.txt", "a", "链 A");
        var tipA = await ResolveAsync(repositoryPath, "HEAD");
        await TestGit.RunAsync(["checkout", "-q", "-b", "chain-b", "main"], repositoryPath);
        await CommitFileAsync(repositoryPath, "b.txt", "b", "链 B");
        var tipB = await ResolveAsync(repositoryPath, "HEAD");

        var check = await CreateService().CheckAncestorAsync(repositoryPath, tipB, tipA);

        Assert.True(check.IsSuccess);
        Assert.False(check.IsAncestor);
    }

    [Fact]
    public async Task 引用不存在时祖先校验返回UnknownReference()
    {
        var repositoryPath = await CreateRepositoryAsync("ancestor-bad-ref", commitCount: 2);
        var headHash = await ResolveAsync(repositoryPath, "HEAD");

        var check = await CreateService().CheckAncestorAsync(
            repositoryPath, "ffffffffffffffffffffffffffffffffffffffff", headHash);

        Assert.False(check.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, check.Failure);
    }

    // ============ 两个新方法的共有失败路径 ============

    [Fact]
    public async Task 引用不存在时范围计算返回UnknownReference()
    {
        var repositoryPath = await CreateRepositoryAsync("range-bad-ref", commitCount: 2);
        var baseHash = await ResolveAsync(repositoryPath, "HEAD");

        var result = await CreateService().GetChangeRangeAsync(
            repositoryPath, baseHash, "ffffffffffffffffffffffffffffffffffffffff");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
    }

    [Fact]
    public async Task 仓库目录不存在时返回DirectoryNotFound()
    {
        var service = CreateService();

        var check = await service.CheckAncestorAsync(@"Z:\不存在仓库", "aaa", "bbb");
        var range = await service.GetChangeRangeAsync(@"Z:\不存在仓库", "aaa", "bbb");

        Assert.Equal(CommitListFailure.DirectoryNotFound, check.Failure);
        Assert.Equal(CommitListFailure.DirectoryNotFound, range.Failure);
    }

    [Fact]
    public async Task Git不可用时返回GitUnavailable()
    {
        var service = new GitService(StubGitEnvironment.NotRunnable(), new GitCliRunner(), SilentLogger);
        var repositoryPath = await CreateRepositoryAsync("no-git", commitCount: 1);

        var check = await service.CheckAncestorAsync(repositoryPath, "aaa", "bbb");
        var range = await service.GetChangeRangeAsync(repositoryPath, "aaa", "bbb");

        Assert.Equal(CommitListFailure.GitUnavailable, check.Failure);
        Assert.Equal(CommitListFailure.GitUnavailable, range.Failure);
    }

    public void Dispose() => _directory.Dispose();

    private GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount = 0, bool bare = false)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount, bare);

    /// <summary>用 rev-parse 解析引用为完整哈希（期望值的交叉验证基准）。</summary>
    private async Task<string> ResolveAsync(string repositoryPath, string reference)
    {
        var output = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["rev-parse", reference], repositoryPath));
        output.EnsureSuccess();
        return output.StandardOutput.Trim();
    }

    private async Task<(string BaseHash, string HeadHash)> ResolveRangeAsync(
        string repositoryPath, string baseReference, string headReference)
        => (await ResolveAsync(repositoryPath, baseReference), await ResolveAsync(repositoryPath, headReference));

    /// <summary>
    /// 把一条 gitlink（模式 160000 的子模块指针）登记进索引并提交，用于造出真实的子模块变化。
    /// 不走 <c>git add .</c>：工作区里没有对应的子模块目录，全量 add 会把指针当成删除暂存。
    /// </summary>
    private static async Task CommitGitlinkAsync(
        string repositoryPath, string path, string targetHash, string message)
    {
        await TestGit.RunAsync(
            ["update-index", "--add", "--cacheinfo", $"160000,{targetHash},{path}"], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
    }

    /// <summary>追加一次文件提交（写文件 → add → commit）。</summary>
    private static async Task CommitFileAsync(string repositoryPath, string fileName, string content, string message)
    {
        var fullPath = Path.Combine(repositoryPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
    }
}
