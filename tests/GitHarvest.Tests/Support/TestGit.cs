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
    /// 执行一条 git 命令并要求成功；失败时抛出带退出码与 stderr 的 <see cref="GitCommandException"/>，
    /// 让造仓库/改仓库状态的测试现场可直接诊断。这是各测试类执行 git 命令的统一入口。
    /// </summary>
    public static async Task RunAsync(IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var result = await new GitCliRunner().RunAsync(Invocation(arguments, workingDirectory));
        result.EnsureSuccess();
    }

    /// <summary>
    /// 执行一条从标准输入读数据的 git 命令（如 <c>git fast-import</c>——批量造数千提交的
    /// 唯一快速手段），并要求成功。GitCliRunner 固定关闭 stdin，所以这条路径单独开进程。
    /// </summary>
    public static async Task RunWithStdinAsync(
        IReadOnlyList<string> arguments,
        string stdinContent,
        string? workingDirectory = null)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 标准输入显式 UTF-8（无 BOM）：默认跟随系统代码页（中文系统是 GBK），
            // 且 Encoding.UTF8 属性自带 BOM——fast-import 会把 BOM 字节当命令解析失败，
            // data 长度按 UTF-8 字节计，编码不一致会让字节计数错位。
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git 进程。");
        try
        {
            await process.StandardInput.WriteAsync(stdinContent).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException exception)
        {
            // 进程在写入过程中退出（流格式非法等）：把 stderr 带进异常，现场可直接诊断。
            var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"向 git 写入标准输入失败：{string.Join(' ', arguments)}；{stderr}",
                exception);
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git 命令失败（退出码 {process.ExitCode}）：{string.Join(' ', arguments)}；{await process.StandardError.ReadToEndAsync().ConfigureAwait(false)}");
        }
    }

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
