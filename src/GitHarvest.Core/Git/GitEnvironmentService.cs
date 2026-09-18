using GitHarvest.Core.Settings;
using Serilog;

namespace GitHarvest.Core.Git;

/// <summary>
/// git 环境自检的实现：按候选顺序（手动指定 → PATH → 常见安装路径）逐个执行 <c>git --version</c>，
/// 第一个通过验证的候选就是结果；全都失败则给出原因与中文引导。
/// 这是「环境有问题也不能让应用崩」的兜底点：除取消外，任何异常都被收敛成不可用状态，绝不外抛。
/// </summary>
public sealed class GitEnvironmentService : IGitEnvironmentService
{
    /// <summary>版本验证命令的参数。</summary>
    private static readonly string[] VersionArguments = ["--version"];

    private readonly GitExecutableLocator _locator;
    private readonly GitCliRunner _runner;
    private readonly IGitExecutablePathProvider _gitPathProvider;
    private readonly ILogger _logger;

    /// <summary>串行化探测并缓存结果：首屏与页面加载可能同时来问，只需探测一次。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GitEnvironmentStatus? _cachedStatus;

    /// <param name="locator">git.exe 候选定位器（PATH 与常见安装路径由它决定）。</param>
    /// <param name="runner">git CLI 调用封装。</param>
    /// <param name="gitPathProvider">全局设置中手动指定的 git.exe 路径来源。</param>
    /// <param name="logger">自检日志：探测成功/失败的版本、来源与路径都写这里。</param>
    public GitEnvironmentService(
        GitExecutableLocator locator,
        GitCliRunner runner,
        IGitExecutablePathProvider gitPathProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(gitPathProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _locator = locator;
        _runner = runner;
        _gitPathProvider = gitPathProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GitEnvironmentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => GetOrProbeAsync(forceRefresh: false, cancellationToken);

    /// <inheritdoc />
    public Task<GitEnvironmentStatus> RefreshAsync(CancellationToken cancellationToken = default)
        => GetOrProbeAsync(forceRefresh: true, cancellationToken);

    /// <summary>
    /// 取缓存结果或重新探测。探测串行化：首屏与页面加载同时来问时只会探测一次；
    /// 强制刷新时忽略缓存（设置里改过 git.exe 路径后用它复验）。
    /// </summary>
    private async Task<GitEnvironmentStatus> GetOrProbeAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cachedStatus is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 等锁期间可能已经有人探测过了，这里再确认一次。
            if (!forceRefresh && _cachedStatus is { } racedStatus)
            {
                return racedStatus;
            }

            return _cachedStatus = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>真正执行一次探测，并把结果写入日志。</summary>
    private async Task<GitEnvironmentStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var manualPath = ReadManualPath();
            var candidates = _locator.FindCandidates(manualPath);

            if (manualPath is not null && candidates.All(candidate => candidate.Source != GitExecutableSource.ManualSetting))
            {
                _logger.Warning(
                    "全局设置里指定的 git.exe 路径不存在或不可用，已回退到自动探测：{ManualPath}",
                    manualPath);
            }

            if (candidates.Count == 0)
            {
                _logger.Warning("未在 PATH 环境变量与常见安装路径中找到 git.exe。");
                return GitEnvironmentStatus.NotFound();
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await TryVerifyAsync(candidate, cancellationToken).ConfigureAwait(false) is { } status)
                {
                    _logger.Information(
                        "git 探测成功：版本 {Version}，来源 {Source}，路径 {ExecutablePath}",
                        status.Version,
                        status.Source,
                        status.ExecutablePath);

                    return status;
                }
            }

            _logger.Error(
                "已找到 {CandidateCount} 个 git.exe 候选，但没有一个能通过 `git --version` 验证：{CandidatePaths}",
                candidates.Count,
                string.Join("；", candidates.Select(candidate => candidate.ExecutablePath)));

            return GitEnvironmentStatus.NotRunnable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 自检是启动路径上的一环：任何意外都收敛成「不可用」，让首页去引导用户。
            _logger.Error(exception, "git 环境自检过程中出现意外错误。");
            return GitEnvironmentStatus.NotRunnable();
        }
    }

    /// <summary>用 <c>git --version</c> 验证一个候选；不可用时返回 <see langword="null"/> 并记 Warning。</summary>
    private async Task<GitEnvironmentStatus?> TryVerifyAsync(
        GitExecutableCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner
                .RunAsync(new GitInvocation(candidate.ExecutablePath, VersionArguments), cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                _logger.Warning(
                    "候选 git.exe 无法执行 `git --version`（退出码 {ExitCode}）：{ExecutablePath}；{Error}",
                    result.ExitCode,
                    candidate.ExecutablePath,
                    result.StandardError.Trim());

                return null;
            }

            if (ParseVersion(result.StandardOutput) is not { } version)
            {
                _logger.Warning(
                    "候选 git.exe 的版本输出无法识别：{ExecutablePath}；{Output}",
                    candidate.ExecutablePath,
                    result.StandardOutput.Trim());

                return null;
            }

            return GitEnvironmentStatus.Available(candidate.ExecutablePath, version, candidate.Source);
        }
        catch (GitCommandException exception)
        {
            _logger.Warning(exception, "候选 git.exe 无法启动：{ExecutablePath}", candidate.ExecutablePath);
            return null;
        }
    }

    /// <summary>读取设置里手动指定的路径；读取本身出错时按未配置处理（自检不该被设置问题拖垮）。</summary>
    private string? ReadManualPath()
    {
        try
        {
            return _gitPathProvider.GitExecutablePath;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "读取全局设置中的 git.exe 路径失败，按未配置处理。");
            return null;
        }
    }

    /// <summary>
    /// 从 <c>git --version</c> 的输出（<c>git version 2.55.0.windows.3</c>）取出版本号本身。
    /// 前缀缺失时退而取首行原文，便于将来 git 改动输出格式时仍能显示点东西。
    /// </summary>
    private static string? ParseVersion(string standardOutput)
    {
        const string prefix = "git version ";

        var firstLine = standardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        return firstLine.StartsWith(prefix, StringComparison.Ordinal)
            ? firstLine[prefix.Length..].Trim()
            : firstLine;
    }
}
