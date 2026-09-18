using GitHarvest.Core.Git;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Git;

/// <summary>
/// git.exe 的三级探测顺序：设置中手动指定（覆盖自动探测）→ PATH 环境变量 → 常见安装路径。
/// 这里只断言候选的顺序、来源与去重，不涉及进程调用。
/// </summary>
public class GitExecutableLocatorTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    [Fact]
    public void 手动指定路径优先于PATH与常见安装路径()
    {
        var manual = CreateGitExecutable("manual");
        var onPath = CreateGitExecutable("on-path");
        var common = CreateGitExecutable(Path.Combine("programfiles", "Git", "cmd"));

        var candidates = CreateLocator([Path.GetDirectoryName(onPath)!], [common])
            .FindCandidates(manual);

        Assert.Equal(
            [
                new GitExecutableCandidate(manual, GitExecutableSource.ManualSetting),
                new GitExecutableCandidate(onPath, GitExecutableSource.PathEnvironment),
                new GitExecutableCandidate(common, GitExecutableSource.CommonInstallPath),
            ],
            candidates);
    }

    [Fact]
    public void 手动指定路径不存在时回落到自动探测()
    {
        var onPath = CreateGitExecutable("on-path");

        var candidates = CreateLocator([Path.GetDirectoryName(onPath)!], [])
            .FindCandidates(Path.Combine(_directory.Path, "missing", "git.exe"));

        var candidate = Assert.Single(candidates);
        Assert.Equal(onPath, candidate.ExecutablePath);
        Assert.Equal(GitExecutableSource.PathEnvironment, candidate.Source);
    }

    [Fact]
    public void PATH未命中时回落到常见安装路径()
    {
        var common = CreateGitExecutable(Path.Combine("programfiles", "Git", "cmd"));
        var emptyPathDirectory = CreateDirectory("empty");

        var candidates = CreateLocator([emptyPathDirectory], [common]).FindCandidates();

        var candidate = Assert.Single(candidates);
        Assert.Equal(common, candidate.ExecutablePath);
        Assert.Equal(GitExecutableSource.CommonInstallPath, candidate.Source);
    }

    [Fact]
    public void 三级都未命中时没有候选()
    {
        Assert.Empty(CreateLocator([], []).FindCandidates());
    }

    [Fact]
    public void 同一路径在多级出现时只保留一次且来源取最高优先级()
    {
        var shared = CreateGitExecutable("shared");

        var candidates = CreateLocator([Path.GetDirectoryName(shared)!], [shared])
            .FindCandidates(shared);

        var candidate = Assert.Single(candidates);
        Assert.Equal(shared, candidate.ExecutablePath);
        Assert.Equal(GitExecutableSource.ManualSetting, candidate.Source);
    }

    [Fact]
    public void 手动指定为目录时解析为该目录下的git()
    {
        var manual = CreateGitExecutable("manual");

        var candidates = CreateLocator([], []).FindCandidates(Path.GetDirectoryName(manual));

        var candidate = Assert.Single(candidates);
        Assert.Equal(manual, candidate.ExecutablePath);
        Assert.Equal(GitExecutableSource.ManualSetting, candidate.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ;; ")]
    public void PATH中的空白条目被忽略(string pathValue)
    {
        Assert.Empty(GitExecutableLocator.SplitPathEnvironment(pathValue));
    }

    [Fact]
    public void PATH按分号切分并去掉引号与空白()
    {
        Assert.Equal(
            [@"C:\one", @"C:\two"],
            GitExecutableLocator.SplitPathEnvironment(@" C:\one ;""C:\two"";"));
    }

    [Fact]
    public void 常见安装路径候选都指向git可执行文件()
    {
        var candidates = GitInstallationPaths.CommonCandidatePaths();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            // 候选里允许带 %ProgramFiles% 这类环境变量，展开后必须是一个绝对的 git.exe 路径。
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            Assert.True(Path.IsPathRooted(expanded), $"候选路径展开后应为绝对路径：{expanded}");
            Assert.Equal("git.exe", Path.GetFileName(expanded), ignoreCase: true);
        });
    }

    public void Dispose() => _directory.Dispose();

    private GitExecutableLocator CreateLocator(
        IEnumerable<string> pathDirectories,
        IEnumerable<string> commonInstallPaths)
        => new(pathDirectories, commonInstallPaths);

    private string CreateDirectory(string relativePath)
    {
        var fullPath = Path.Combine(_directory.Path, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    /// <summary>在测试目录下造一个占位 git.exe（只关心路径存在，不执行它）。</summary>
    private string CreateGitExecutable(string relativeDirectory)
    {
        var directory = CreateDirectory(relativeDirectory);
        var executablePath = Path.Combine(directory, "git.exe");
        File.WriteAllText(executablePath, string.Empty);
        return executablePath;
    }
}
