using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHarvest.Core.Infrastructure;
using Serilog;

namespace GitHarvest.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> 的 JSON 文件实现：settings.json 存全局设置，
/// repository-state.json 存每仓库状态与最近仓库列表（仓库相关状态同文件，均为人手可读的 JSON）。
/// 读写约定：
/// - 解析宽容（注释、尾逗号、字段名大小写不敏感），因为文件是给人手改的；
/// - 缺失/损坏回退默认值并记 Warning，不崩溃；损坏的原文件保留不覆写，留给用户修复；
/// - 写入先落同目录临时文件再原子替换，进程中断不会留下半个 JSON；
/// - 全部成员线程安全（ViewModel 与后台探测可能并发访问）。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>
    /// 序列化选项取自公共约定（<see cref="JsonFileOptions"/> 的缩进档）：宽容解析、中文不转义、缩进输出。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = JsonFileOptions.HumanEditable;

    private readonly object _gate = new();
    private readonly string _settingsFilePath;
    private readonly string _repositoryStateFilePath;
    private readonly ILogger _logger;

    private GlobalSettings _settings;
    private RepositoryStateDocument _state;

    /// <summary>构造时一次性加载 settings.json 与 repository-state.json，之后所有访问都走内存快照。</summary>
    /// <param name="settingsFilePath">settings.json 的路径，取自 <c>IDataLocation.SettingsFilePath</c>
    /// （数据目录可被改到别处，因此不能在这里自己拼 %APPDATA%）。</param>
    /// <param name="repositoryStateFilePath">每仓库状态文件的路径，取自 <c>IDataLocation.RepositoryStateFilePath</c>。</param>
    /// <param name="logger">设置读写日志（缺失/损坏回退记 Warning，设置保存记 Info）。</param>
    public SettingsService(string settingsFilePath, string repositoryStateFilePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryStateFilePath);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsFilePath = settingsFilePath;
        _repositoryStateFilePath = repositoryStateFilePath;
        _logger = logger;

        // 构造时一次性加载：之后的所有访问都走内存快照，文件只在本类的写入路径里被触碰。
        _settings = LoadSettings();
        _state = LoadState();
    }

    /// <inheritdoc />
    public GlobalSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    /// <inheritdoc />
    public void Save(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings);

        lock (_gate)
        {
            // 先落盘成功才更新内存快照：保存失败时内存与磁盘保持一致，用户重试即可，
            // 不会出现「界面显示已保存、实际盘上还是旧值」的分裂。
            // 与每仓库状态的写入不同，这里是用户的显式保存动作，失败必须抛给调用方反馈。
            WriteAtomically(_settingsFilePath, JsonSerializer.Serialize(normalized, SerializerOptions));
            _settings = normalized;
        }

        // 「设置变更」是 spec 约定要记 Info 的关键操作之一。
        _logger.Information("全局设置已更新并保存：{SettingsFilePath}", _settingsFilePath);
    }

    /// <inheritdoc />
    public RepositoryState GetRepositoryState(string repositoryPath)
    {
        var key = NormalizeRepositoryKey(repositoryPath);

        lock (_gate)
        {
            return _state.Repositories.TryGetValue(key, out var state) ? state : new RepositoryState();
        }
    }

    /// <inheritdoc />
    public void SaveRepositoryState(string repositoryPath, RepositoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var key = NormalizeRepositoryKey(repositoryPath);

        lock (_gate)
        {
            _state.Repositories[key] = Normalize(state);
            PersistStateQuietly();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RecentRepositories
    {
        get
        {
            lock (_gate)
            {
                // 返回快照，调用方持有的是副本，不会绕过本接口改动内部状态。
                return _state.RecentRepositories.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void AddRecentRepository(string repositoryPath)
    {
        var key = NormalizeRepositoryKey(repositoryPath);

        lock (_gate)
        {
            // 同一仓库（任意写法）先移除旧条目再置顶——「最新在前」且不重复。
            _state.RecentRepositories.RemoveAll(item =>
                string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
            _state.RecentRepositories.Insert(0, key);
            TruncateRecentToLimit(_state.RecentRepositories);

            PersistStateQuietly();
        }
    }

    /// <inheritdoc />
    public string? GitExecutablePath
    {
        get
        {
            lock (_gate)
            {
                var value = _settings.GitExecutablePath;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        lock (_gate)
        {
            // 与构造时同一段加载逻辑：文件缺失会按默认值重新生成（首次启动的既有行为），
            // 损坏则回退默认并保留原文件——「重置全部数据」之后正是这个路径。
            _settings = LoadSettings();
            _state = LoadState();
        }

        _logger.Information("已从磁盘重新加载全局设置与每仓库状态：{SettingsFilePath}", _settingsFilePath);
    }

    /// <summary>
    /// 读取 settings.json。缺失时记 Warning 并生成默认文件（首次启动的「自动生成」）；解析或读取失败时
    /// 记 Warning 并回退默认值——原文件保留不覆写，用户修复语法后无需任何操作即可恢复。
    /// </summary>
    private GlobalSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.Warning("全局设置文件不存在，已按默认值生成：{SettingsFilePath}", _settingsFilePath);

                var defaults = new GlobalSettings();
                WriteAtomically(_settingsFilePath, JsonSerializer.Serialize(defaults, SerializerOptions));
                return defaults;
            }

            var document = JsonSerializer.Deserialize<GlobalSettings>(
                File.ReadAllText(_settingsFilePath),
                SerializerOptions);

            return Normalize(document ?? new GlobalSettings());
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Warning(
                exception,
                "全局设置文件损坏或不可读，已回退默认值（原文件保留待人工修复）：{SettingsFilePath}",
                _settingsFilePath);

            return new GlobalSettings();
        }
    }

    /// <summary>
    /// 读取每仓库状态文件。文件尚未生成属正常情况（首次写入时才落盘），静默返回空数据；
    /// 解析或读取失败时记 Warning 并回退空数据，原文件保留不覆写。
    /// </summary>
    private RepositoryStateDocument LoadState()
    {
        try
        {
            if (!File.Exists(_repositoryStateFilePath))
            {
                return new RepositoryStateDocument();
            }

            var document = JsonSerializer.Deserialize<RepositoryStateDocument>(
                File.ReadAllText(_repositoryStateFilePath),
                SerializerOptions);

            return NormalizeLoadedState(document ?? new RepositoryStateDocument());
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Warning(
                exception,
                "每仓库状态文件损坏或不可读，已回退空数据（原文件保留待人工修复）：{RepositoryStateFilePath}",
                _repositoryStateFilePath);

            return new RepositoryStateDocument();
        }
    }

    /// <summary>
    /// 落盘每仓库状态与最近列表。与「保存全局设置」不同：状态是辅助记忆，写入失败
    /// （磁盘满、文件被占用等）降级为 Warning、不向调用方抛出——否则选分支、记录输出路径
    /// 这类常规操作可能因 IO 问题炸掉 UI 流程。内存快照保持已更新：本次会话内继续按新值
    /// 运行，重启后回落到磁盘上的旧值（可从日志发现）。
    /// </summary>
    private void PersistStateQuietly()
    {
        try
        {
            WriteAtomically(_repositoryStateFilePath, JsonSerializer.Serialize(_state, SerializerOptions));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Warning(
                exception,
                "每仓库状态写入失败，本次会话内仍按新值运行，重启后将回落到旧值：{RepositoryStateFilePath}",
                _repositoryStateFilePath);
        }
    }

    /// <summary>
    /// 清洗手改/旧版本文件的数据：仓库键归一化并用大小写不敏感比较器重建字典，
    /// 最近列表归一化、去重并截断到上限——手改文件哪怕写法不规范也不会破坏行为。
    /// </summary>
    private static RepositoryStateDocument NormalizeLoadedState(RepositoryStateDocument document)
    {
        var repositories = new Dictionary<string, RepositoryState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, state) in document.Repositories)
        {
            var key = TryNormalizeRepositoryKey(path);
            if (key is not null)
            {
                repositories[key] = Normalize(state ?? new RepositoryState());
            }
        }

        var recent = new List<string>();
        foreach (var path in document.RecentRepositories)
        {
            var key = TryNormalizeRepositoryKey(path);
            if (key is not null && !recent.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                recent.Add(key);
            }
        }

        TruncateRecentToLimit(recent);

        return new RepositoryStateDocument { RecentRepositories = recent, Repositories = repositories };
    }

    /// <summary>把最近仓库列表截断到上限（保留最新在前的前 N 条）。「上限 10 条」策略只写在这一处。</summary>
    private static void TruncateRecentToLimit(List<string> recent)
    {
        if (recent.Count > ISettingsService.MaxRecentRepositories)
        {
            recent.RemoveRange(
                ISettingsService.MaxRecentRepositories,
                recent.Count - ISettingsService.MaxRecentRepositories);
        }
    }

    /// <summary>归一化每仓库状态的文本字段（去首尾空白，空白与 null 按空串存）。</summary>
    private static RepositoryState Normalize(RepositoryState state) => new()
    {
        LastOutputPath = NormalizeText(state.LastOutputPath),
        LastBranch = NormalizeText(state.LastBranch),
    };

    /// <summary>
    /// 把仓库路径归一化为状态字典的键。规则与导出历史共用 <see cref="RepositoryPathKey"/>
    /// ——同一个仓库在「设置记住的分支」与「导出历史里的最近导出」之间必须认同一套写法。
    /// </summary>
    private static string NormalizeRepositoryKey(string repositoryPath)
        => RepositoryPathKey.Normalize(repositoryPath);

    /// <summary>容错版键归一化：手改文件里的怪路径不值得让加载失败，丢弃该条目即可。</summary>
    private static string? TryNormalizeRepositoryKey(string? repositoryPath)
        => RepositoryPathKey.TryNormalize(repositoryPath);

    /// <summary>把反序列化出来的设置归一化：路径字段去首尾空白，空白与 null 一律按「未配置」存为空串。</summary>
    private static GlobalSettings Normalize(GlobalSettings settings) => new()
    {
        DefaultOutputPath = NormalizeText(settings.DefaultOutputPath),
        DefaultTemplatePath = NormalizeText(settings.DefaultTemplatePath),
        DefaultTheme = settings.DefaultTheme,
        GitExecutablePath = NormalizeText(settings.GitExecutablePath),
        // 保留天数：负数一律归 0（= 不自动清理）。0 表达「不清理」是设置页的滑块/输入框能选到的值，
        // 而负数没有任何意义，手改文件写 -1 时按用户最可能的本意（别删）处理。
        DataRetentionDays = Math.Max(0, settings.DataRetentionDays),
    };

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// 先把内容写入同目录的临时文件，再原子替换到目标路径，避免进程中断留下半个 JSON。
    /// 实现见 <see cref="AtomicFile"/>——数据目录指针文件也用同一段代码。
    /// </summary>
    private static void WriteAtomically(string filePath, string content)
        => AtomicFile.WriteAllText(filePath, content);

    /// <summary>repository-state.json 的文件结构（内部契约）：最近仓库列表 + 按仓库根路径索引的状态字典。</summary>
    private sealed record RepositoryStateDocument
    {
        /// <summary>最近打开的仓库根路径，最新在前。</summary>
        [JsonPropertyName("recentRepositories")]
        public List<string> RecentRepositories { get; init; } = [];

        /// <summary>
        /// 按仓库根路径索引的每仓库状态。初始化器的大小写不敏感比较器只对代码新建的空文档生效，
        /// 反序列化产出的字典由 <see cref="NormalizeLoadedState"/> 统一重建，两条路径的语义保持一致。
        /// </summary>
        [JsonPropertyName("repositories")]
        public Dictionary<string, RepositoryState> Repositories { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
