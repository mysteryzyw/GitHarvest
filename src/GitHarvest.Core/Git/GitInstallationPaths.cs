namespace GitHarvest.Core.Git;

/// <summary>
/// Git for Windows 等常见安装布局下的 git.exe 位置。
/// 这里只给候选，不做存在性判断（判断在 <see cref="GitExecutableLocator"/> 里统一做）。
/// 路径里的环境变量在解析时展开：变量不存在的机器上该候选自然落空。
/// </summary>
public static class GitInstallationPaths
{
    /// <summary>git.exe 的文件名。</summary>
    public const string ExecutableFileName = "git.exe";

    /// <summary>
    /// Git for Windows 安装程序的标准布局（cmd 目录是加进 PATH 的那一个），
    /// 以及 Scoop、Chocolatey 这类包管理器常见的落地位置。
    /// </summary>
    private static readonly string[] CandidatePaths =
    [
        // Git for Windows：默认装到 Program Files（32 位安装器落在 Program Files (x86)）
        @"%ProgramFiles%\Git\cmd\git.exe",
        @"%ProgramFiles(x86)%\Git\cmd\git.exe",
        @"%ProgramW6432%\Git\cmd\git.exe",
        // Git for Windows：用户级安装
        @"%LOCALAPPDATA%\Programs\Git\cmd\git.exe",
        // 老版本安装器的布局（没有 cmd 目录，可执行文件在 bin 下）
        @"%ProgramFiles%\Git\bin\git.exe",
        @"%LOCALAPPDATA%\Programs\Git\bin\git.exe",
        // 包管理器
        @"%USERPROFILE%\scoop\apps\git\current\cmd\git.exe",
        @"%ProgramData%\chocolatey\bin\git.exe",
    ];

    /// <summary>常见安装路径候选（可能包含未展开的环境变量，由定位器展开后判断存在性）。</summary>
    public static IReadOnlyList<string> CommonCandidatePaths() => CandidatePaths;
}
