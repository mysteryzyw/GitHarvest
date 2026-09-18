using System.Globalization;
using Serilog;

namespace GitHarvest.Core.Git;

/// <summary>
/// <see cref="IGitService"/> 的 git CLI 实现：打开仓库 = 一小组 <c>git rev-parse</c> /
/// <c>git rev-list</c> 调用的编排与解读。git.exe 路径取自环境自检的缓存结果（不自行探测），
/// 进程调用全部经 <see cref="GitCliRunner"/>（统一编码、取消与错误收敛）。
/// </summary>
public sealed class GitService : IGitService
{
    private readonly IGitEnvironmentService _gitEnvironment;
    private readonly GitCliRunner _runner;
    private readonly ILogger _logger;

    /// <param name="gitEnvironment">git 环境自检：从这里取已验证的 git.exe 路径（结果有缓存，不重复起进程）。</param>
    /// <param name="runner">git CLI 调用封装。</param>
    /// <param name="logger">打开结果与失败原因都写这里，便于排查。</param>
    public GitService(IGitEnvironmentService gitEnvironment, GitCliRunner runner, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);

        _gitEnvironment = gitEnvironment;
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RepositoryOpenResult> OpenRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // 目录不存在时进程根本没有有效的工作目录（git 都还没机会报错），先自查给出准确提示。
        if (!Directory.Exists(repositoryPath))
        {
            _logger.Warning("打开仓库失败，目录不存在：{RepositoryPath}", repositoryPath);
            return RepositoryOpenResult.Failed(
                RepositoryOpenFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}");
        }

        var status = await _gitEnvironment.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.ExecutablePath is not { } gitPath)
        {
            return RepositoryOpenResult.Failed(
                RepositoryOpenFailure.GitUnavailable,
                "Git 尚未就绪，无法打开仓库：请先按首页引导安装或配置 Git。");
        }

        // 第一步：这个目录是不是 Git 仓库。git 对「目录不存在」「不是仓库」给出
        // 不同的英文报错（LC_ALL=C 保证稳定），据此区分失败类别，让界面能给出准确提示。
        var probe = await RunGitAsync(
            gitPath,
            ["rev-parse", "--is-bare-repository"],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!probe.IsSuccess)
        {
            return ClassifyProbeFailure(repositoryPath, probe);
        }

        var isBare = probe.StandardOutput.Trim() == "true";

        // 第二步：仓库根。git 会从传入目录向上找到真正的仓库，所以从子目录打开也能解析出根。
        // bare 仓库没有工作区，--show-toplevel 会失败，改用 git 目录本身（bare 仓库的根就是它）；
        // git 输出正斜杠风格，用 GetFullPath 归一成 Windows 风格再交给上层使用。
        var rootQuery = isBare ? "--absolute-git-dir" : "--show-toplevel";
        var root = await RunGitAsync(
            gitPath,
            ["rev-parse", rootQuery],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!root.IsSuccess)
        {
            return UnknownGitError(root);
        }

        var rootPath = Path.GetFullPath(root.StandardOutput.Trim());

        // 第三步：当前分支。游离头指针（detached HEAD）时输出为空，返回 null 让界面标注「（游离）」。
        var branch = await RunGitAsync(
            gitPath,
            ["branch", "--show-current"],
            rootPath,
            cancellationToken).ConfigureAwait(false);

        var currentBranch = branch.IsSuccess && branch.StandardOutput.Trim().Length > 0
            ? branch.StandardOutput.Trim()
            : null;

        // 第四步：提交总数。空仓库（unborn HEAD）会失败且报 unknown revision——这属于正常形态，
        // 提交数按 0 处理；其他失败说明仓库状态异常，按 Git 错误处理而不是悄悄当成 0。
        var commitCount = 0;
        var revList = await RunGitAsync(
            gitPath,
            ["rev-list", "--count", "HEAD"],
            rootPath,
            cancellationToken).ConfigureAwait(false);

        if (revList.IsSuccess)
        {
            commitCount = int.Parse(revList.StandardOutput.Trim(), CultureInfo.InvariantCulture);
        }
        else if (!revList.StandardError.Contains("unknown revision", StringComparison.Ordinal))
        {
            return UnknownGitError(revList);
        }

        var repository = new RepositoryInfo(rootPath, isBare, currentBranch, commitCount);

        _logger.Information(
            "仓库打开成功：{RootPath}（{RepositoryKind}，分支 {CurrentBranch}，提交数 {CommitCount}）",
            repository.RootPath,
            isBare ? "bare" : "普通",
            repository.CurrentBranch ?? "（游离）",
            repository.CommitCount);

        return RepositoryOpenResult.Succeeded(repository);
    }

    /// <summary>
    /// 把「是否仓库」探测的失败归类：目录不存在与「不是仓库」分别提示，
    /// 其余按 Git 错误透出技术细节（写入日志便于排查）。
    /// </summary>
    private RepositoryOpenResult ClassifyProbeFailure(string repositoryPath, GitCommandResult probe)
    {
        var stderr = probe.StandardError.Trim();

        if (stderr.Contains("cannot change to", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("打开仓库失败，目录不存在：{RepositoryPath}", repositoryPath);
            return RepositoryOpenResult.Failed(
                RepositoryOpenFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}",
                stderr);
        }

        if (stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("打开仓库失败，不是 Git 仓库：{RepositoryPath}", repositoryPath);
            return RepositoryOpenResult.Failed(
                RepositoryOpenFailure.NotARepository,
                $"所选文件夹不是 Git 仓库：{repositoryPath}",
                stderr);
        }

        return UnknownGitError(probe);
    }

    /// <summary>无法归类的 Git 错误：界面给出通用提示，stderr 完整进日志。</summary>
    private RepositoryOpenResult UnknownGitError(GitCommandResult result)
    {
        var stderr = result.StandardError.Trim();
        _logger.Warning(
            "Git 命令失败（退出码 {ExitCode}）：{CommandLine}；{StandardError}",
            result.ExitCode,
            result.Invocation.CommandLineText,
            stderr);

        return RepositoryOpenResult.Failed(
            RepositoryOpenFailure.GitError,
            $"打开仓库时 Git 返回错误（退出码 {result.ExitCode}），详情请查看日志。",
            stderr);
    }

    private Task<GitCommandResult> RunGitAsync(
        string gitPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
        => _runner.RunAsync(new GitInvocation(gitPath, arguments, workingDirectory), cancellationToken);
}
