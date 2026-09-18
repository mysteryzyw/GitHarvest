using GitHarvest.Core.Git;

namespace GitHarvest.Tests.Support;

/// <summary>
/// 集成测试用的本机 git.exe：按产品自身的探测顺序（PATH → 常见安装路径）定位。
/// 这些测试跑真实 Git，本机没装 Git 时直接失败并说明原因，而不是静默跳过——
/// 本项目的核心行为就是驱动 git.exe，跳过等于不验证。
/// </summary>
internal static class TestGit
{
    private static readonly Lazy<string> LazyExecutablePath = new(Locate);

    /// <summary>本机 git.exe 的绝对路径。</summary>
    public static string ExecutablePath => LazyExecutablePath.Value;

    /// <summary>为指定工作目录构造一条 git 命令。</summary>
    public static GitInvocation Invocation(IEnumerable<string> arguments, string? workingDirectory = null)
        => new(ExecutablePath, [.. arguments], workingDirectory);

    /// <summary>
    /// 在指定目录下造一个名为 git.exe 但不是可执行文件的占位文件：路径存在，启动进程必然失败。
    /// 用来验证「找到了 git.exe 却不可执行」的分支，返回它的完整路径。
    /// </summary>
    public static string CreateUnrunnable(string directory)
    {
        Directory.CreateDirectory(directory);

        var executablePath = Path.Combine(directory, "git.exe");
        File.WriteAllText(executablePath, "不是一个可执行文件");
        return executablePath;
    }

    private static string Locate()
        => GitExecutableLocator.ForCurrentEnvironment().FindCandidates().FirstOrDefault()?.ExecutablePath
           ?? throw new InvalidOperationException(
               "本机未找到 git.exe（PATH 环境变量与常见安装路径都没有），依赖真实 Git 的集成测试无法运行。");
}
