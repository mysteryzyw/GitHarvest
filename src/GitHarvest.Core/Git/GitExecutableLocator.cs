namespace GitHarvest.Core.Git;

/// <summary>
/// 按优先级定位 git.exe：手动指定的路径 → PATH 环境变量 → 常见安装路径。
/// 只产出「路径确实存在」的候选并按优先级排序、去重；能否真正执行由环境自检逐个验证。
/// 文件系统与 PATH 内容都从构造参数注入，便于在没有 Git 的机器上验证探测顺序。
/// </summary>
public sealed class GitExecutableLocator
{
    private readonly IReadOnlyList<string> _pathDirectories;
    private readonly IReadOnlyList<string> _commonInstallPaths;

    /// <param name="pathDirectories">PATH 环境变量中的目录（顺序即 PATH 中的顺序）。</param>
    /// <param name="commonInstallPaths">常见安装路径候选，见 <see cref="GitInstallationPaths.CommonCandidatePaths"/>。</param>
    public GitExecutableLocator(
        IEnumerable<string> pathDirectories,
        IEnumerable<string> commonInstallPaths)
    {
        ArgumentNullException.ThrowIfNull(pathDirectories);
        ArgumentNullException.ThrowIfNull(commonInstallPaths);

        _pathDirectories = [.. pathDirectories];
        _commonInstallPaths = [.. commonInstallPaths];
    }

    /// <summary>用当前进程的 PATH 与 <see cref="GitInstallationPaths"/> 建立定位器。</summary>
    public static GitExecutableLocator ForCurrentEnvironment()
        => new(
            SplitPathEnvironment(Environment.GetEnvironmentVariable("PATH")),
            GitInstallationPaths.CommonCandidatePaths());

    /// <summary>
    /// 按优先级返回所有存在的 git.exe 候选（同一路径只保留优先级最高的那一级）。
    /// </summary>
    /// <param name="manualExecutablePath">全局设置里手动指定的路径；可为空、可以是 git.exe 也可只是它所在的目录。</param>
    public IReadOnlyList<GitExecutableCandidate> FindCandidates(string? manualExecutablePath = null)
    {
        var candidates = new List<GitExecutableCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(manualExecutablePath, GitExecutableSource.ManualSetting);

        foreach (var directory in _pathDirectories)
        {
            AddCandidate(Path.Combine(directory, GitInstallationPaths.ExecutableFileName), GitExecutableSource.PathEnvironment);
        }

        foreach (var path in _commonInstallPaths)
        {
            AddCandidate(path, GitExecutableSource.CommonInstallPath);
        }

        return candidates;

        /// <summary>解析一个候选：存在且首次出现时收进结果（重复路径保留优先级更高的那一级）。</summary>
        void AddCandidate(string? rawPath, GitExecutableSource source)
        {
            if (ResolveExecutablePath(rawPath) is not { } executablePath)
            {
                return;
            }

            if (seen.Add(executablePath))
            {
                candidates.Add(new GitExecutableCandidate(executablePath, source));
            }
        }
    }

    /// <summary>
    /// 把 PATH 环境变量的值切成目录列表：按分号切分，剔除空白项与包裹的双引号（Windows 的 PATH 里
    /// 带空格的目录有时会被引号包住）。
    /// </summary>
    /// <param name="pathValue">PATH 环境变量的原始值。</param>
    public static IReadOnlyList<string> SplitPathEnvironment(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return [];
        }

        return
        [
            .. pathValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Trim('"'))
                .Where(entry => !string.IsNullOrWhiteSpace(entry)),
        ];
    }

    /// <summary>
    /// 把候选解析成存在的 git.exe 绝对路径：展开环境变量、去掉引号，指向目录时补上 git.exe。
    /// 不存在（或解析不出绝对路径）时返回 <see langword="null"/>。
    /// </summary>
    private static string? ResolveExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return null;
        }

        try
        {
            // 宽容处理：设置里可能填的是 git.exe 所在的目录（例如 C:\Program Files\Git\cmd）。
            var executablePath = Directory.Exists(expanded)
                ? Path.Combine(expanded, GitInstallationPaths.ExecutableFileName)
                : expanded;

            return File.Exists(executablePath) ? Path.GetFullPath(executablePath) : null;
        }
        catch (ArgumentException)
        {
            // 路径里有非法字符：这一级候选直接作废，继续看下一级。
            return null;
        }
    }
}
