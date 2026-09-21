namespace GitHarvest.Core.Settings;

/// <summary>
/// 全部 JSON 持久化的唯一出口（settings.json 与每仓库状态文件）：
/// 全局设置读写、每仓库状态（上次输出路径/上次分支）存取、最近打开仓库列表（上限 10 条）。
/// 持久化文件均可人手编辑。容错口径分三条：
/// 读取路径（构造加载与查询）在文件损坏或缺失时回退默认值并记 Warning，绝不抛出、更不让应用崩溃；
/// <see cref="Save"/> 是用户的显式保存动作，写入失败向上抛出，由调用方反馈用户；
/// 每仓库状态与最近列表的写入失败降级为 Warning（内存快照仍生效），不打断常规操作流程。
/// 继承 <see cref="IGitExecutablePathProvider"/>：git 三级探测由此读取手动路径，
/// 设置实现落地后探测逻辑无需任何改动（ticket 02 约定的接管点）。
/// </summary>
public interface ISettingsService : IGitExecutablePathProvider
{
    /// <summary>最近打开仓库列表的上限，超出时淘汰最旧的条目。</summary>
    public const int MaxRecentRepositories = 10;

    /// <summary>当前生效的全局设置（损坏/缺失时为默认值）。返回不可变快照，外部无法绕过本接口改动它。</summary>
    GlobalSettings Settings { get; }

    /// <summary>用给定设置替换当前值并立即持久化（设置页在用户确认保存时调用）。</summary>
    void Save(GlobalSettings settings);

    /// <summary>
    /// 读取指定仓库记住的状态；从未记录过时返回默认值（而非 <see langword="null"/>）。
    /// </summary>
    /// <param name="repositoryPath">仓库根目录路径；大小写与分隔符差异视为同一仓库。</param>
    RepositoryState GetRepositoryState(string repositoryPath);

    /// <summary>保存指定仓库记住的状态并立即持久化。</summary>
    void SaveRepositoryState(string repositoryPath, RepositoryState state);

    /// <summary>最近打开的仓库（最新在前，去重，最多 <see cref="MaxRecentRepositories"/> 条）。</summary>
    IReadOnlyList<string> RecentRepositories { get; }

    /// <summary>把仓库加入（或移到）最近列表首位并立即持久化。</summary>
    void AddRecentRepository(string repositoryPath);

    /// <summary>
    /// 从磁盘重新加载设置与每仓库状态，丢弃内存快照。
    /// 用于「磁盘被外部改动」的场景——设置页的「重置全部数据」删掉了这些文件，
    /// 而内存里还留着旧值，不重载就会出现「界面显示已重置、首页仍显示旧统计」的分裂。
    /// 文件缺失时按默认值重新生成（与首次启动同一路径），读取失败仍按容错口径回退并记 Warning。
    /// </summary>
    void Reload();
}
