using GitHarvest.Core.Git;
using GitHarvest.Core.Interaction;

namespace GitHarvest.Shell;

/// <summary>
/// <see cref="IRepositorySession"/> 的壳实现：DI 单例，纯内存会话状态，应用退出即失效。
/// 无属性变更通知——页面与 ViewModel 都是 Transient，每次导航新建时读取当前值即可。
/// 草稿与「本次输出路径」的失效规则集中在这里：
/// 换仓库后范围、说明草稿与本次输出路径一并清空（都只对上一个仓库有意义）；
/// 换范围只清说明草稿（草稿是按旧范围生成的，恢复旧稿会把别的范围的内容写进本次更新说明），
/// 输出路径与范围无关——同一仓库里换一对提交，用户选的目的地仍然算数。
/// </summary>
public sealed class RepositorySession : IRepositorySession
{
    private RepositoryInfo? _openedRepository;
    private RangeSelection? _selectedRange;
    private string? _notesDraft;
    private string? _sessionOutputPath;

    /// <inheritdoc />
    public RepositoryInfo? OpenedRepository
    {
        get => _openedRepository;
        set
        {
            if (Equals(_openedRepository, value))
            {
                return;
            }

            _openedRepository = value;
            _selectedRange = null;
            _notesDraft = null;
            _sessionOutputPath = null;
        }
    }

    /// <inheritdoc />
    public RangeSelection? SelectedRange
    {
        get => _selectedRange;
        set
        {
            // 第 2 步每次选中变化都会重写一个新实例：值相等（同两个提交）时保留草稿，
            // 否则「回到上一步再点下一步」会把用户没改选择时的编辑内容误清掉。
            if (Equals(_selectedRange, value))
            {
                return;
            }

            _selectedRange = value;
            _notesDraft = null;
        }
    }

    /// <inheritdoc />
    public string? NotesDraft
    {
        get => _notesDraft;
        set => _notesDraft = value;
    }

    /// <inheritdoc />
    public string? SessionOutputPath
    {
        get => _sessionOutputPath;
        set => _sessionOutputPath = value;
    }
}
