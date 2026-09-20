using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GitHarvest.Core.Git;

/// <summary>
/// git CLI 调用的唯一进程出口：所有 git 命令都经这里执行，调用方只面对
/// <see cref="GitCommandResult"/>，不接触 <see cref="Process"/>。
/// 统一约定：
/// <list type="bullet">
///   <item>环境变量 <c>LC_ALL=C</c>：锁定输出语言，git 的报错与提示始终是英文，可被程序判断。</item>
///   <item>环境变量 <c>GIT_TERMINAL_PROMPT=0</c>：禁止终端凭据提示，需要凭据时即时失败而非弹 GUI 卡死。</item>
///   <item>标准输出/错误一律按 UTF-8 解码：中文路径与提交信息不会乱码；
///     非 ASCII 路径的转义问题由调用方加 <c>-z</c> 规避（<see cref="GitCommandResult.GetNulSeparatedRecords"/>）。</item>
///   <item>退出码与 stderr 收敛到 <see cref="GitCommandResult"/>，失败用 <see cref="GitCommandResult.EnsureSuccess"/> 抛出。</item>
///   <item>异步执行并接受取消令牌，取消时终止整个进程树并抛 <see cref="OperationCanceledException"/>。</item>
/// </list>
/// 不负责定位 git.exe：可执行文件路径由探测（<see cref="GitExecutableLocator"/>）或设置提供。
/// </summary>
public sealed class GitCliRunner
{
    /// <summary>锁定 git 输出语言的环境变量值。</summary>
    public const string LocaleValue = "C";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 执行一条 git 命令。
    /// </summary>
    /// <param name="invocation">要执行的命令。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止进程树。</param>
    /// <returns>命令结果（含退出码与两条输出流），不因退出码非 0 而抛异常。</returns>
    /// <exception cref="GitCommandException">进程无法启动（路径不存在、不是可执行文件等）。</exception>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    public async Task<GitCommandResult> RunAsync(
        GitInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.ExecutablePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Start(invocation, decodeStandardOutputAsText: true);

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);

            // 进程已终止，两条输出流随即读完；这里等它们收尾，避免留下未观察的任务。
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            throw;
        }

        return new GitCommandResult(
            invocation,
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    /// <summary>
    /// 执行一条 git 命令，并把标准输出**按原始字节**写入 <paramref name="destination"/>。
    /// 与 <see cref="RunAsync"/> 的唯一区别是不做文本解码：文件内容可能是二进制，
    /// 按 UTF-8 解码会把非法字节换成 U+FFFD，写出的快照就不再是仓库里的内容了
    /// （导出快照用 <c>git cat-file blob</c> 取内容，走的就是这条路径）。
    /// </summary>
    /// <param name="invocation">要执行的命令。</param>
    /// <param name="destination">标准输出的落点流（由调用方打开与关闭）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止进程树。</param>
    /// <returns>
    /// 命令结果：<see cref="GitCommandResult.StandardOutput"/> 恒为空串（输出进了目标流），
    /// <see cref="GitCommandResult.StandardError"/> 与退出码照常，便于按既有口径归类失败。
    /// </returns>
    /// <exception cref="GitCommandException">进程无法启动（路径不存在、不是可执行文件等）。</exception>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    public async Task<GitCommandResult> RunToStreamAsync(
        GitInvocation invocation,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.ExecutablePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Start(invocation, decodeStandardOutputAsText: false);

        // 标准错误必须并发抽干：stderr 管道写满会让 git 阻塞，而我们正在等 stdout 结束，互相死等。
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await standardError.ConfigureAwait(false);
            throw;
        }

        return new GitCommandResult(invocation, process.ExitCode, string.Empty, await standardError.ConfigureAwait(false));
    }

    /// <summary>
    /// 启动 git 进程并关闭它的标准输入；两条路径共用的部分都在这里（环境变量、工作目录、
    /// 启动失败的收敛、禁止终端提示）。
    /// </summary>
    /// <param name="invocation">要执行的命令。</param>
    /// <param name="decodeStandardOutputAsText">
    /// 是否按 UTF-8 解码标准输出（流式取二进制内容时必须为假）。
    /// </param>
    private static Process Start(GitInvocation invocation, bool decodeStandardOutputAsText)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Utf8,
        };

        if (decodeStandardOutputAsText)
        {
            startInfo.StandardOutputEncoding = Utf8;
        }

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(invocation.WorkingDirectory))
        {
            startInfo.WorkingDirectory = invocation.WorkingDirectory;
        }

        startInfo.Environment["LC_ALL"] = LocaleValue;

        // 禁止 git 在终端里询问凭据或确认：碰到需要凭据的远程（fetch 等场景）即时失败，
        // 而不是让 Git Credential Manager 弹出 GUI 把后台流程卡死（ticket 02 定下的交接项，
        // 对所有调用统一生效——本地操作本就不需要终端提示，禁用无副作用）。
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            process.Dispose();
            throw new GitCommandException(
                $"无法启动 git 进程：{invocation.ExecutablePath}（{exception.Message}）",
                result: null,
                exception);
        }

        // 关闭标准输入：git 需要凭据或确认时立即失败，而不是静静地等待输入。
        process.StandardInput.Close();

        return process;
    }

    /// <summary>取消时终止进程树；进程已退出或无法终止时保持静默（取消语义不受影响）。</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // 进程已自行退出或无法终止：取消语义不变，交给调用方处理。
        }
    }
}
