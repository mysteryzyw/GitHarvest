using System.Text;
using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// 按 blob 取文件内容的集成测试（真实 git.exe）：导出快照靠它把「更新前 / 更新后」的
/// 文件内容原样写出来，因此这里要盯住两件事——内容与仓库里的字节完全一致（中文、二进制
/// 都不能被编码处理弄坏），以及失败被归类成结果而不是异常。
/// </summary>
public sealed class CopyBlobTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    [Fact]
    public async Task 文本内容与提交里的字节一致()
    {
        var repositoryPath = await CreateRepositoryAsync("blob-text");
        var content = "第一行：中文\n第二行：tab\t与引号 \"x\"\n";
        await CommitFileAsync(repositoryPath, "说明.txt", Encoding.UTF8.GetBytes(content), "c1");
        var blobHash = await ResolveAsync(repositoryPath, "HEAD:说明.txt");

        using var destination = new MemoryStream();
        var result = await CreateService().CopyBlobToAsync(repositoryPath, blobHash, destination);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(Encoding.UTF8.GetBytes(content), destination.ToArray());
    }

    [Fact]
    public async Task 二进制内容逐字节一致()
    {
        var repositoryPath = await CreateRepositoryAsync("blob-binary");
        // 含 NUL 与 0xFF：任何按文本解码再编码的处理都会在这里被替换掉。
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFE, 0x1A, 0x0A, 0x00, 0x01];
        await CommitFileAsync(repositoryPath, "logo.bin", content, "c1");
        var blobHash = await ResolveAsync(repositoryPath, "HEAD:logo.bin");

        using var destination = new MemoryStream();
        var result = await CreateService().CopyBlobToAsync(repositoryPath, blobHash, destination);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(content, destination.ToArray());
    }

    [Fact]
    public async Task 空文件也正常取出()
    {
        var repositoryPath = await CreateRepositoryAsync("blob-empty");
        await CommitFileAsync(repositoryPath, "empty.txt", [], "c1");
        var blobHash = await ResolveAsync(repositoryPath, "HEAD:empty.txt");

        using var destination = new MemoryStream();
        var result = await CreateService().CopyBlobToAsync(repositoryPath, blobHash, destination);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task 对象不存在时返回UnknownReference()
    {
        var repositoryPath = await CreateRepositoryAsync("blob-missing", commitCount: 1);

        using var destination = new MemoryStream();
        var result = await CreateService().CopyBlobToAsync(
            repositoryPath,
            "0000000000000000000000000000000000000000",
            destination);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommitListFailure.UnknownReference, result.Failure);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public async Task 仓库目录不存在时返回DirectoryNotFound()
    {
        using var destination = new MemoryStream();

        var result = await CreateService().CopyBlobToAsync(@"Z:\不存在仓库", "aaa", destination);

        Assert.Equal(CommitListFailure.DirectoryNotFound, result.Failure);
    }

    [Fact]
    public async Task Git不可用时返回GitUnavailable()
    {
        var repositoryPath = await CreateRepositoryAsync("blob-no-git", commitCount: 1);
        var service = new GitService(StubGitEnvironment.NotRunnable(), new GitCliRunner(), SilentLogger);

        using var destination = new MemoryStream();
        var result = await service.CopyBlobToAsync(repositoryPath, "aaa", destination);

        Assert.Equal(CommitListFailure.GitUnavailable, result.Failure);
    }

    public void Dispose() => _directory.Dispose();

    private GitService CreateService()
        => new(StubGitEnvironment.Available(), new GitCliRunner(), SilentLogger);

    private Task<string> CreateRepositoryAsync(string name, int commitCount = 0)
        => TestRepository.CreateAsync(_directory.Path, name, commitCount);

    private async Task<string> ResolveAsync(string repositoryPath, string reference)
    {
        var output = await new GitCliRunner().RunAsync(
            TestGit.Invocation(["rev-parse", reference], repositoryPath));
        output.EnsureSuccess();
        return output.StandardOutput.Trim();
    }

    private static async Task CommitFileAsync(string repositoryPath, string fileName, byte[] content, string message)
    {
        await File.WriteAllBytesAsync(Path.Combine(repositoryPath, fileName), content);
        await TestGit.RunAsync(["add", "."], repositoryPath);
        await TestGit.RunAsync(["commit", "-q", "-m", message], repositoryPath);
    }
}
