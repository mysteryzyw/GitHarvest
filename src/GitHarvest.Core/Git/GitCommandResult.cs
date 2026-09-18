namespace GitHarvest.Core.Git;

/// <summary>
/// 一次 git 调用的结果：退出码、标准输出与标准错误（均按 UTF-8 解码）。
/// 失败判断收敛在 <see cref="IsSuccess"/> 与 <see cref="EnsureSuccess"/> 上，
/// 调用方不需要各自去解释退出码。
/// </summary>
/// <param name="Invocation">被执行的命令。</param>
/// <param name="ExitCode">进程退出码，0 表示成功。</param>
/// <param name="StandardOutput">标准输出全文（UTF-8 解码）。</param>
/// <param name="StandardError">标准错误全文（UTF-8 解码，LC_ALL=C 下为英文）。</param>
public sealed record GitCommandResult(
    GitInvocation Invocation,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    /// <summary>命令是否成功（退出码为 0）。</summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// 把使用 <c>-z</c> 的 git 命令输出切成记录。git 用空字符分隔记录，空字符同时意味着
    /// 「此处不做转义」，因此中文路径与提交信息是不带引号的原始 UTF-8 文本。
    /// </summary>
    public IReadOnlyList<string> GetNulSeparatedRecords()
        => StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>命令失败时抛出 <see cref="GitCommandException"/>，成功时什么都不做。</summary>
    /// <exception cref="GitCommandException">退出码非 0。</exception>
    public void EnsureSuccess()
    {
        if (!IsSuccess)
        {
            throw new GitCommandException(
                $"Git 命令失败（退出码 {ExitCode}）：{Invocation.CommandLineText}{Environment.NewLine}{StandardError.Trim()}",
                this);
        }
    }
}
