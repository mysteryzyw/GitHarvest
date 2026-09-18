namespace GitHarvest.Core.Git;

/// <summary>
/// 一次 git 调用：用哪个可执行文件、带什么参数、在哪个工作目录下跑。
/// 工作目录留空时继承当前进程的工作目录；操作某个仓库的命令必须显式传仓库路径。
/// </summary>
/// <param name="ExecutablePath">git.exe 的绝对路径（由探测或设置提供）。</param>
/// <param name="Arguments">命令行参数（不含可执行文件本身），按顺序传给 git。</param>
/// <param name="WorkingDirectory">工作目录；<see langword="null"/> 表示继承当前进程的工作目录。</param>
public sealed record GitInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null)
{
    /// <summary>供日志与异常消息使用的命令行文本（只用于展示，不用于执行）。</summary>
    public string CommandLineText
    {
        get
        {
            var fileName = string.IsNullOrWhiteSpace(ExecutablePath)
                ? "git"
                : Path.GetFileName(ExecutablePath);

            return Arguments.Count == 0
                ? fileName
                : $"{fileName} {string.Join(' ', Arguments.Select(QuoteIfNeeded))}";
        }
    }

    /// <summary>
    /// 只在参数含空格时加引号：这段文本仅供日志与异常阅读，不当作可直接执行的命令行，
    /// 因此不需要还原 Windows 命令行的那套转义规则。
    /// </summary>
    private static string QuoteIfNeeded(string argument)
        => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
