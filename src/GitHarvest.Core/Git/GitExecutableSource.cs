namespace GitHarvest.Core.Git;

/// <summary>
/// git.exe 的探测来源，取值顺序即优先级（越小的值越先被采用）。
/// </summary>
public enum GitExecutableSource
{
    /// <summary>全局设置中手动指定的路径。存在即覆盖自动探测结果。</summary>
    ManualSetting = 1,

    /// <summary>PATH 环境变量中的目录。</summary>
    PathEnvironment = 2,

    /// <summary>已知的常见安装路径（见 <see cref="GitInstallationPaths"/>）。</summary>
    CommonInstallPath = 3,
}
