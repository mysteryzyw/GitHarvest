using CommunityToolkit.Mvvm.ComponentModel;
using GitHarvest.Core.Git;

namespace GitHarvest.ViewModels;

/// <summary>
/// 「提交详情」卡的状态：展示最后点选的那个提交的完整预览
/// （完整信息、作者、时间、父提交与 diffstat），数据来自 <see cref="IGitService.GetCommitDetailAsync"/>。
/// 未选中、加载中与读取失败都是明确的状态而非静默空白。
/// </summary>
public sealed partial class CommitPreviewViewModel : ObservableObject
{
    private readonly IGitService _gitService;

    /// <summary>竞态令牌：连续点选不同提交时，慢的旧详情不覆盖新的。</summary>
    private int _generation;

    public CommitPreviewViewModel(IGitService gitService)
    {
        ArgumentNullException.ThrowIfNull(gitService);
        _gitService = gitService;
    }

    /// <summary>当前预览的提交（列表项信息，详情读取前的即时反馈）；无选中时为 null。</summary>
    [ObservableProperty]
    private CommitItem? _selectedCommit;

    /// <summary>读取到的完整详情；尚未读到（加载中/失败）时为 null。</summary>
    [ObservableProperty]
    private CommitDetail? _detail;

    /// <summary>详情读取失败时的提示文案。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>详情正在读取。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>是否有被点选的提交（驱动详情卡「未选中」提示的显隐）。</summary>
    public bool HasSelection => SelectedCommit is not null;

    /// <summary>是否显示详情正文（读取成功）。</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>是否显示失败提示。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>作者时间的展示文案（本地时间，详情里要的是可读的绝对时间而非相对时间）。</summary>
    public string AuthorTimeText =>
        Detail is { } detail ? detail.AuthorTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : string.Empty;

    /// <summary>父提交的展示文案（短哈希并列；根提交说明「无（根提交）」）。</summary>
    public string ParentText
    {
        get
        {
            if (Detail is not { } detail)
            {
                return string.Empty;
            }

            return detail.ParentHashes.Count == 0
                ? "无（根提交）"
                : string.Join("、", detail.ParentHashes.Select(hash => hash.Length > 7 ? hash[..7] : hash));
        }
    }

    /// <summary>diffstat 的展示文案（「3 个文件变更，+10 行，-2 行」；零增删按「无增删行」说明）。</summary>
    public string DiffStatText
    {
        get
        {
            if (Detail is not { } detail)
            {
                return string.Empty;
            }

            var stat = detail.DiffStat;
            return $"{stat.FilesChanged} 个文件变更，+{stat.Additions} 行，-{stat.Deletions} 行";
        }
    }

    /// <summary>切换要预览的提交并异步读取详情。</summary>
    public async Task LoadAsync(string repositoryRoot, CommitItem? commit)
    {
        SelectedCommit = commit;
        var generation = ++_generation;

        if (commit is null)
        {
            Detail = null;
            ErrorMessage = null;
            IsLoading = false;
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _gitService.GetCommitDetailAsync(repositoryRoot, commit.ShortHash).ConfigureAwait(true);
            if (generation != _generation || !ReferenceEquals(SelectedCommit, commit))
            {
                return;
            }

            if (result is not { Detail: { } detail })
            {
                Detail = null;
                ErrorMessage = result.FailureMessage;
                return;
            }

            Detail = detail;
        }
        catch (OperationCanceledException)
        {
            // 详情读取没有界面取消入口，窗口关闭场景的兜底。
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    partial void OnDetailChanged(CommitDetail? value)
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(AuthorTimeText));
        OnPropertyChanged(nameof(ParentText));
        OnPropertyChanged(nameof(DiffStatText));
    }

    partial void OnSelectedCommitChanged(CommitItem? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
