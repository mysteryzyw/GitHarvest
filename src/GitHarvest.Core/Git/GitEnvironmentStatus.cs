namespace GitHarvest.Core.Git;

/// <summary>
/// git 环境自检的结果：要么可用（带可执行文件路径、版本与探测来源），
/// 要么不可用（带原因与给用户看的中文引导）。两种状态互斥，因此只经
/// <see cref="Available"/> / <see cref="NotFound"/> / <see cref="NotRunnable"/> 构造。
/// </summary>
public sealed record GitEnvironmentStatus
{
    private GitEnvironmentStatus(
        bool isAvailable,
        GitExecutableSource? source,
        string? executablePath,
        string? version,
        GitUnavailableReason reason,
        string? guidance)
    {
        IsAvailable = isAvailable;
        Source = source;
        ExecutablePath = executablePath;
        Version = version;
        Reason = reason;
        Guidance = guidance;
    }

    /// <summary>git 是否可用。</summary>
    public bool IsAvailable { get; }

    /// <summary>可执行文件的来源；不可用时为 <see langword="null"/>。</summary>
    public GitExecutableSource? Source { get; }

    /// <summary>git.exe 的绝对路径；不可用时为 <see langword="null"/>。</summary>
    public string? ExecutablePath { get; }

    /// <summary>git 版本号（如 <c>2.55.0.windows.3</c>）；不可用时为 <see langword="null"/>。</summary>
    public string? Version { get; }

    /// <summary>不可用的原因；可用时为 <see cref="GitUnavailableReason.None"/>。</summary>
    public GitUnavailableReason Reason { get; }

    /// <summary>给用户看的中文引导（未找到 Git 时怎么写、去哪里手动指定）；可用时为 <see langword="null"/>。</summary>
    public string? Guidance { get; }

    /// <summary>自检通过。</summary>
    public static GitEnvironmentStatus Available(string executablePath, string version, GitExecutableSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return new GitEnvironmentStatus(
            isAvailable: true,
            source,
            executablePath,
            version,
            GitUnavailableReason.None,
            guidance: null);
    }

    /// <summary>三级探测都没找到 git.exe。</summary>
    public static GitEnvironmentStatus NotFound()
        => new(
            isAvailable: false,
            source: null,
            executablePath: null,
            version: null,
            GitUnavailableReason.NotFound,
            "未找到 Git：请先安装 Git for Windows（https://git-scm.com/download/win）后重启应用，"
            + "或在「全局设置」中手动指定 git.exe 的路径。");

    /// <summary>找到了 git.exe，但都无法执行 <c>git --version</c>。</summary>
    public static GitEnvironmentStatus NotRunnable()
        => new(
            isAvailable: false,
            source: null,
            executablePath: null,
            version: null,
            GitUnavailableReason.NotRunnable,
            "找到的 Git 无法执行：它可能已损坏或不是真正的 git.exe，"
            + "请在「全局设置」中手动指定可用的 git.exe 路径。");
}
