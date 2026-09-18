namespace GitHarvest.Core.Git;

/// <summary>
/// git 调用失败：命令跑了但退出码非 0，或者进程根本启动不了。
/// 消息里始终带上命令行与 git 自己的错误输出，避免调用方再拼一遍上下文。
/// </summary>
public sealed class GitCommandException : Exception
{
    /// <param name="message">包含命令行与 git 错误输出的中文消息。</param>
    /// <param name="result">失败时的命令结果；进程未能启动时为 <see langword="null"/>。</param>
    /// <param name="innerException">进程启动失败时的底层异常。</param>
    public GitCommandException(string message, GitCommandResult? result = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Result = result;
    }

    /// <summary>失败时的命令结果；进程未能启动时为 <see langword="null"/>。</summary>
    public GitCommandResult? Result { get; }
}
