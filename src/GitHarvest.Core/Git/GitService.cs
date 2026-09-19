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

    /// <inheritdoc />
    public async Task<BranchListResult> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            _logger.Warning("读取分支列表失败，目录不存在：{RepositoryPath}", repositoryPath);
            return BranchListResult.Failed(
                BranchListFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}");
        }

        var status = await _gitEnvironment.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.ExecutablePath is not { } gitPath)
        {
            return BranchListResult.Failed(
                BranchListFailure.GitUnavailable,
                "Git 尚未就绪，无法读取分支列表：请先按首页引导安装或配置 Git。");
        }

        // 第一步：是否游离头指针。symbolic-ref -q HEAD 在分支上时成功并输出引用全名，游离时退出码 1。
        var head = await RunGitAsync(
            gitPath, ["symbolic-ref", "-q", "HEAD"], repositoryPath, cancellationToken).ConfigureAwait(false);

        // 第二步：枚举本地（refs/heads）与远程跟踪（refs/remotes）引用。
        // 字段间用 %00（NUL）分隔：字段值不可能含 NUL，%(subject) 只是信息首行也不含换行，
        // 所以按行切记录、按 NUL 切字段即可；NUL 同时意味着中文分支名与提交信息不做转义。
        const string format =
            "%(refname)%00%(refname:short)%00%(objectname:short)%00%(subject)%00" +
            "%(authorname)%00%(authordate:iso-strict)%00%(HEAD)%00%(symref)";
        var refs = await RunGitAsync(
            gitPath,
            ["for-each-ref", $"--format={format}", "refs/heads", "refs/remotes"],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!refs.IsSuccess)
        {
            return BranchListResult.Failed(
                BranchListFailure.GitError,
                $"读取分支列表时 Git 返回错误（退出码 {refs.ExitCode}），详情请查看日志。",
                LogGitFailure(refs));
        }

        var branches = new List<BranchInfo>();
        foreach (var line in refs.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\0');
            if (fields.Length != 8)
            {
                _logger.Warning("Git 的分支引用输出无法解析（字段数 {FieldCount}）：{Line}", fields.Length, line);
                return BranchListResult.Failed(
                    BranchListFailure.GitError,
                    "Git 的分支列表输出无法解析，详情请查看日志。",
                    line);
            }

            // symref 非空的是 origin/HEAD 这类符号引用——它只是「远程默认分支」的别名，
            // 不是独立候选，否则列表里会出现一行意义不明的 origin/HEAD。
            if (fields[7].Length > 0)
            {
                continue;
            }

            var tip = new CommitSummary(
                fields[2],
                fields[3],
                fields[4],
                DateTimeOffset.Parse(fields[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

            branches.Add(new BranchInfo(
                fields[1],
                IsRemote: fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal),
                IsCurrent: fields[6] == "*",
                IsDetached: false,
                tip));
        }

        // 游离头指针不是分支，但它是合法的「基准位置」：补一个候选项放在列表最前，
        // 由界面标注「（游离）」（用户故事：不丢失这个状态）。
        if (!head.IsSuccess)
        {
            if (await ReadTipAsync(gitPath, repositoryPath, "HEAD", cancellationToken).ConfigureAwait(false)
                is not { } detachedTip)
            {
                return BranchListResult.Failed(
                    BranchListFailure.GitError,
                    "读取游离头指针的提交摘要时 Git 返回错误，详情请查看日志。",
                    null);
            }

            branches.Insert(0, new BranchInfo("HEAD", IsRemote: false, IsCurrent: false, IsDetached: true, detachedTip));
        }

        _logger.Information(
            "分支列表读取完成：{RepositoryPath}（{BranchCount} 个候选{DetachedNote}）",
            repositoryPath,
            branches.Count,
            head.IsSuccess ? string.Empty : "，含游离头指针");

        return BranchListResult.Succeeded(branches);
    }

    /// <inheritdoc />
    public async Task<FetchResult> FetchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            _logger.Warning("拉取失败，目录不存在：{RepositoryPath}", repositoryPath);
            return FetchResult.Failed(
                FetchFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}");
        }

        var status = await _gitEnvironment.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.ExecutablePath is not { } gitPath)
        {
            return FetchResult.Failed(
                FetchFailure.GitUnavailable,
                "Git 尚未就绪，无法拉取：请先按首页引导安装或配置 Git。");
        }

        // 没有配置远程的仓库无从拉取——这不是崩溃，给明确结果让界面解释。
        var remotes = await RunGitAsync(gitPath, ["remote"], repositoryPath, cancellationToken).ConfigureAwait(false);
        if (!remotes.IsSuccess)
        {
            return FetchResult.Failed(
                FetchFailure.GitError,
                $"查询远程列表时 Git 返回错误（退出码 {remotes.ExitCode}），详情请查看日志。",
                LogGitFailure(remotes));
        }

        if (remotes.StandardOutput.Trim().Length == 0)
        {
            _logger.Information("拉取跳过，仓库没有配置远程：{RepositoryPath}", repositoryPath);
            return FetchResult.Failed(
                FetchFailure.NoRemote,
                "该仓库没有配置远程，没有可拉取的内容。");
        }

        // spec 的「拉取」语义：git fetch 只更新远程跟踪分支，不 merge、不动工作区。
        // 需要凭据的远程会因 GIT_TERMINAL_PROMPT=0 即时失败（不弹凭据界面卡死），按 Git 错误处理。
        var fetch = await RunGitAsync(gitPath, ["fetch"], repositoryPath, cancellationToken).ConfigureAwait(false);
        if (!fetch.IsSuccess)
        {
            return FetchResult.Failed(
                FetchFailure.GitError,
                $"拉取远程更新时 Git 返回错误（退出码 {fetch.ExitCode}），详情请查看日志。",
                LogGitFailure(fetch));
        }

        _logger.Information("拉取完成，远程跟踪分支已更新：{RepositoryPath}", repositoryPath);
        return FetchResult.Succeeded();
    }

    /// <summary>
    /// 取某个引用最新提交的摘要（与分支列表同一组字段）；失败时返回 <see langword="null"/> 并记日志。
    /// 用于游离头指针——它不是任何 ref，for-each-ref 枚举不到，需单独按 HEAD 取。
    /// </summary>
    private async Task<CommitSummary?> ReadTipAsync(
        string gitPath,
        string repositoryPath,
        string reference,
        CancellationToken cancellationToken)
    {
        var tip = await RunGitAsync(
            gitPath,
            ["log", "-1", "--format=%h%x00%s%x00%an%x00%aI", reference],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!tip.IsSuccess)
        {
            LogGitFailure(tip);
            return null;
        }

        var fields = tip.StandardOutput.TrimEnd('\n').Split('\0');
        if (fields.Length != 4)
        {
            _logger.Warning("Git 的提交摘要输出无法解析（字段数 {FieldCount}）：{Output}", fields.Length, tip.StandardOutput);
            return null;
        }

        return new CommitSummary(
            fields[0],
            fields[1],
            fields[2],
            DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>把一次失败的 git 调用写入日志（退出码 + 命令行 + stderr），返回 stderr 摘要供结果对象携带。</summary>
    private string LogGitFailure(GitCommandResult result)
    {
        var stderr = result.StandardError.Trim();
        _logger.Warning(
            "Git 命令失败（退出码 {ExitCode}）：{CommandLine}；{StandardError}",
            result.ExitCode,
            result.Invocation.CommandLineText,
            stderr);

        return stderr;
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
        => RepositoryOpenResult.Failed(
            RepositoryOpenFailure.GitError,
            $"打开仓库时 Git 返回错误（退出码 {result.ExitCode}），详情请查看日志。",
            LogGitFailure(result));

    private Task<GitCommandResult> RunGitAsync(
        string gitPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
        => _runner.RunAsync(new GitInvocation(gitPath, arguments, workingDirectory), cancellationToken);
}
