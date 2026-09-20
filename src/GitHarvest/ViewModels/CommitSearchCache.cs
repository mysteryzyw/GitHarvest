using GitHarvest.Core.Git;
using Serilog;

namespace GitHarvest.ViewModels;

/// <summary>
/// 选择提交页两栏共享的提交搜索缓存：首次搜索时后台把分支历史**整份读一次**，
/// 之后两栏的搜索过滤与滚动翻页都在内存里做——不再每次输入/翻页都重启一次 git 全量读取
/// （这是搜索「慢」的根源）。
/// 只在 UI 线程访问（VM 的 async/await 都回到 UI 上下文），不加锁。
/// 生命周期与页面一致；切分支或拉取后由宿主调用 <see cref="Invalidate"/> 失效重建。
/// </summary>
public sealed class CommitSearchCache
{
    /// <summary>全量读取的扫描上限（与 Core 的保护上限一致）：超出部分不参与过滤。</summary>
    private const int ScanLimit = 100_000;

    private readonly IGitService _gitService;
    private readonly ILogger _logger;

    private string? _repositoryRoot;
    private string? _reference;

    /// <summary>已读到的整份提交（新提交在前）；<see langword="null"/> 表示尚未加载。</summary>
    private IReadOnlyList<CommitSummary>? _items;

    /// <summary>进行中的全量读取——两栏同时搜索时共用同一个任务，不会各起一次 git 进程。</summary>
    private Task<IReadOnlyList<CommitSummary>>? _loading;

    public CommitSearchCache(IGitService gitService, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(logger);

        _gitService = gitService;
        _logger = logger;
    }

    /// <summary>缓存是否已就绪（搜索的完整结果可直接从内存取出）。</summary>
    public bool IsReady => _items is not null;

    /// <summary>全量读取进行中（界面据此显示「正在检索全部提交…」）。</summary>
    public bool IsLoading => _loading is { IsCompleted: false };

    /// <summary>是否已绑定到某个仓库 / 引用（未绑定时不发起读取）。</summary>
    public bool HasSource => _repositoryRoot is not null && _reference is not null;

    /// <summary>
    /// 失效并改绑到新的仓库 / 引用（切分支、拉取后调用）：清空缓存与在途读取，
    /// 下次搜索时重新整份读取。无效入参（空仓库 / 空引用）时清空且视为未绑定。
    /// </summary>
    public void Invalidate(string? repositoryRoot, string? reference)
    {
        _repositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot) ? null : repositoryRoot;
        _reference = string.IsNullOrWhiteSpace(reference) ? null : reference;
        _items = null;
        _loading = null;
    }

    /// <summary>
    /// 取整份提交：已就绪直接返回；正在读取则共用同一个任务；否则发起一次全量读取
    /// （受扫描上限保护）。
    /// 读取失败返回空列表（界面据此显示「没有匹配的提交」），失败原因进日志。
    /// </summary>
    public Task<IReadOnlyList<CommitSummary>> GetOrLoadAsync()
    {
        if (_items is { } ready)
        {
            return Task.FromResult(ready);
        }

        if (_loading is { } inFlight)
        {
            return inFlight;
        }

        if (!HasSource)
        {
            return Task.FromResult<IReadOnlyList<CommitSummary>>([]);
        }

        _loading = LoadAsync();
        return _loading;
    }

    private async Task<IReadOnlyList<CommitSummary>> LoadAsync()
    {
        var result = await _gitService
            .GetCommitsAsync(_repositoryRoot!, _reference!, new CommitQuery(Search: null, Offset: 0, Limit: ScanLimit))
            .ConfigureAwait(true);

        if (result is not { Commits: { } commits })
        {
            _logger.Warning("提交搜索缓存读取失败：{FailureMessage}", result.FailureMessage);
            _items = [];
            return _items;
        }

        if (result.HasMore)
        {
            // 达到扫描上限：更早的提交不参与搜索，值得留痕（正常仓库不会触发）。
            _logger.Warning(
                "提交搜索缓存已达扫描上限（{Count} 条），更早的提交不参与搜索：{RepositoryRoot} @ {Reference}",
                commits.Count,
                _repositoryRoot,
                _reference);
        }

        _items = commits;
        return _items;
    }
}
