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

    /// <inheritdoc />
    public async Task<CommitListResult> GetCommitsAsync(
        string repositoryPath,
        string reference,
        CommitQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(query);
        if (!query.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "分页偏移不能为负、页大小必须为正。");
        }

        if (!Directory.Exists(repositoryPath))
        {
            _logger.Warning("读取提交列表失败，目录不存在：{RepositoryPath}", repositoryPath);
            return CommitListResult.Failed(
                CommitListFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}");
        }

        var status = await _gitEnvironment.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.ExecutablePath is not { } gitPath)
        {
            return CommitListResult.Failed(
                CommitListFailure.GitUnavailable,
                "Git 尚未就绪，无法读取提交列表：请先按首页引导安装或配置 Git。");
        }

        // 列表项与分支 Tip 同一组字段（复用 CommitSummary）：短哈希/信息首行/作者/作者时间。
        // 字段间 %00 分隔、按行切记录——%(subject) 只有信息首行不含换行，字段值不可能含 NUL，
        // 中文提交信息与作者不做转义（与分支枚举同一手法）。
        // --topo-order：合并历史上按日期倒排（git log 默认）会把两条线的提交交错显示，
        // 读起来分不清谁在谁之后、谁属于哪条线——拓扑序把同一线的提交聚在一起、父提交恒在
        // 子提交之后，与 merge-base 的祖先结论口径一致（第 2 步选基准/Head 时不会误判祖先关系）。
        // 分页不受影响：--skip/--max-count 作用在同一份拓扑序结果上。
        const string format = "%h%x00%s%x00%an%x00%aI";
        var logArguments = new List<string> { "log", "--topo-order", $"--format={format}" };

        var search = query.Search?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            // 无过滤词（浏览的主力路径）：分页下推给 git，--skip/--max-count 只取一页 + 1 条探测下一页。
            logArguments.Add($"--skip={query.Offset}");
            logArguments.Add($"--max-count={query.Limit + 1}");
        }
        else
        {
            // 有过滤词（一次性动作）：全量读取（受扫描上限保护）后在内存里过滤——
            // 实测 git 的 --grep 与 --author 同给时是 AND 而非 OR，与界面「哈希 / 信息 / 作者
            // 任一命中」的语义不符，故过滤统一在内存里做。
            logArguments.Add("--max-count=" + MaxScanCommitCount);
        }

        logArguments.Add(reference);

        var log = await RunGitAsync(gitPath, logArguments, repositoryPath, cancellationToken).ConfigureAwait(false);
        if (!log.IsSuccess)
        {
            return ClassifyCommitReadFailure(reference, log);
        }

        var page = ParseCommitSummaries(log.StandardOutput);
        if (page is null)
        {
            return CommitListResult.Failed(
                CommitListFailure.GitError,
                "Git 的提交列表输出无法解析，详情请查看日志。");
        }

        bool hasMore;
        if (string.IsNullOrEmpty(search))
        {
            // 多读的那 1 条只用来探测下一页，不属于本页结果。
            hasMore = page.Count > query.Limit;
            if (hasMore)
            {
                page = [.. page.Take(query.Limit)];
            }
        }
        else
        {
            // 有过滤词：全量读取（受扫描上限保护）后在内存按「哈希 / 信息 / 作者」三字段过滤
            // （共用 CommitSearchFilter——与界面输入即刻的渐进过滤是同一份实现），
            // 再在过滤后的结果集上切片，分页对调用方透明。
            var matchedAll = CommitSearchFilter.Filter(page, search);
            hasMore = matchedAll.Count > query.Offset + query.Limit;
            page = [.. matchedAll.Skip(query.Offset).Take(query.Limit)];
        }

        _logger.Information(
            "提交列表读取完成：{RepositoryPath} @ {Reference}（本页 {Count} 条{SearchNote}）",
            repositoryPath,
            reference,
            page.Count,
            string.IsNullOrEmpty(search) ? string.Empty : $"，过滤词「{search}」");

        return CommitListResult.Succeeded(page, hasMore);
    }

    /// <inheritdoc />
    public async Task<CommitDetailResult> GetCommitDetailAsync(
        string repositoryPath,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        if (!Directory.Exists(repositoryPath))
        {
            _logger.Warning("读取提交详情失败，目录不存在：{RepositoryPath}", repositoryPath);
            return CommitDetailResult.Failed(
                CommitListFailure.DirectoryNotFound,
                $"仓库目录不存在：{repositoryPath}");
        }

        var status = await _gitEnvironment.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.ExecutablePath is not { } gitPath)
        {
            return CommitDetailResult.Failed(
                CommitListFailure.GitUnavailable,
                "Git 尚未就绪，无法读取提交详情：请先按首页引导安装或配置 Git。");
        }

        // 第一步：提交元数据。-s 只要信息不出 diff；%B 是完整信息（含标题后的正文与换行），
        // 放在最后一个字段，它的内部换行不会破坏前六个字段的 NUL 切分。
        var metadata = await RunGitAsync(
            gitPath,
            ["show", "-s", "--format=%H%x00%h%x00%s%x00%an%x00%aI%x00%P%x00%B", hash],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!metadata.IsSuccess)
        {
            var failure = ClassifyCommitReadFailure(hash, metadata);
            return CommitDetailResult.Failed(failure.Failure, failure.FailureMessage!, failure.TechnicalDetail);
        }

        var record = metadata.StandardOutput.TrimEnd('\n');
        var fields = record.Split('\0', 7);
        if (fields.Length != 7)
        {
            _logger.Warning("Git 的提交详情输出无法解析（字段数 {FieldCount}）：{Output}", fields.Length, record);
            return CommitDetailResult.Failed(
                CommitListFailure.GitError,
                "Git 的提交详情输出无法解析，详情请查看日志。");
        }

        // 父提交：空格分隔的完整哈希；根提交为空字符串 → 空列表。
        var parentHashes = fields[5]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 第二步：diffstat。合并提交的 git show 不含对第一父的 diff，
        // 统一按「有父 → diff 第一父..提交，根提交 → show（相对空树）」取 numstat，自己汇总数字。
        var numstatArguments = parentHashes.Length > 0
            ? new[] { "diff", "--numstat", parentHashes[0], fields[0] }
            : new[] { "show", "--numstat", "--format=", fields[0] };

        var numstat = await RunGitAsync(gitPath, numstatArguments, repositoryPath, cancellationToken).ConfigureAwait(false);
        if (!numstat.IsSuccess)
        {
            var stderr = numstat.StandardError.Trim();
            _logger.Warning(
                "Git 命令失败（退出码 {ExitCode}）：{CommandLine}；{StandardError}",
                numstat.ExitCode,
                numstat.Invocation.CommandLineText,
                stderr);
            return CommitDetailResult.Failed(
                CommitListFailure.GitError,
                $"读取提交的变更统计时 Git 返回错误（退出码 {numstat.ExitCode}），详情请查看日志。",
                stderr);
        }

        var detail = new CommitDetail(
            fields[0],
            fields[1],
            fields[2],
            fields[6],
            fields[3],
            DateTimeOffset.Parse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            parentHashes,
            SummarizeNumstat(numstat.StandardOutput));

        _logger.Information(
            "提交详情读取完成：{RepositoryPath} @ {ShortHash}（父提交 {ParentCount} 个，{Files} 个文件）",
            repositoryPath,
            detail.ShortHash,
            detail.ParentHashes.Count,
            detail.DiffStat.FilesChanged);

        return CommitDetailResult.Succeeded(detail);
    }

    /// <summary>把 numstat 输出汇总成 diffstat 数字：二进制行（"-" 占位）计入文件数但不计增删行数。</summary>
    private static CommitDiffStat SummarizeNumstat(string output)
    {
        var filesChanged = 0;
        var additions = 0;
        var deletions = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                continue;
            }

            filesChanged++;
            if (int.TryParse(fields[0], CultureInfo.InvariantCulture, out var added))
            {
                additions += added;
            }

            if (int.TryParse(fields[1], CultureInfo.InvariantCulture, out var deleted))
            {
                deletions += deleted;
            }
        }

        return new CommitDiffStat(filesChanged, additions, deletions);
    }

    /// <summary>把 git log --format 的输出解析为提交摘要列表；出现异常形态（字段数不对）时返回 null。</summary>
    private List<CommitSummary>? ParseCommitSummaries(string output)
    {
        var commits = new List<CommitSummary>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\0');
            if (fields.Length != 4)
            {
                _logger.Warning("Git 的提交列表输出无法解析（字段数 {FieldCount}）：{Line}", fields.Length, line);
                return null;
            }

            commits.Add(new CommitSummary(
                fields[0],
                fields[1],
                fields[2],
                DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return commits;
    }

    /// <summary>
    /// 把提交列表 / 详情读取的失败归类：引用不存在（分支被删、哈希打错）单独提示，
    /// 其余按 Git 错误透出技术细节。
    /// </summary>
    private CommitListResult ClassifyCommitReadFailure(string reference, GitCommandResult result)
    {
        var stderr = result.StandardError.Trim();

        // 「引用不存在」的两种 Git 报法：短名解析不了是 unknown revision，
        // 完整哈希查无此对象是 bad object——对用户是同一件事：找不到这个提交。
        if (stderr.Contains("unknown revision", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("bad object", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("ambiguous argument", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("读取提交失败，引用不存在：{Reference}", reference);
            return CommitListResult.Failed(
                CommitListFailure.UnknownReference,
                $"找不到提交「{reference}」：该分支或提交哈希在当前仓库中不存在。",
                stderr);
        }

        _logger.Warning(
            "Git 命令失败（退出码 {ExitCode}）：{CommandLine}；{StandardError}",
            result.ExitCode,
            result.Invocation.CommandLineText,
            stderr);

        return CommitListResult.Failed(
            CommitListFailure.GitError,
            $"读取提交时 Git 返回错误（退出码 {result.ExitCode}），详情请查看日志。",
            stderr);
    }

    /// <summary>
    /// 搜索时的全量读取扫描上限：保护内存不被异常巨大的仓库耗尽（交付场景的仓库远小于此）。
    /// 超出上限的历史不参与过滤，界面上的搜索结果可能不完整。
    /// </summary>
    private const int MaxScanCommitCount = 100_000;

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
