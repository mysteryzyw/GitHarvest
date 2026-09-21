using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GitHarvest.Core.Infrastructure;
using Serilog;

namespace GitHarvest.Core.History;

/// <summary>
/// <see cref="IHistoryService"/> 的 JSONL 文件实现（%APPDATA%\GitHarvest\export-history.jsonl）：
/// 一行一条 JSON 记录，**只追加不改写**——历史文件被写坏时损失最小（最多丢最后半行），
/// 且人手打开就能读、能修。文件里所有内容在构造时读进内存快照，之后的查询不再碰磁盘，
/// 首页同一次渲染里多个统计值必然来自同一份数据。
/// 容错口径见 <see cref="IHistoryService"/>：任何读写问题都只记 Warning，绝不打断导出与首页。
/// </summary>
public sealed class HistoryService : IHistoryService
{
    /// <summary>
    /// 序列化选项取自公共约定（<see cref="JsonFileOptions"/> 的逐行档）：宽容解析、中文不转义，
    /// 但**不缩进**——一行一条记录，缩进会把文件撑大且不再是一行一条。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = JsonFileOptions.LineDelimited;

    /// <summary>UTF-8 无 BOM：与更新说明、设置文件同一取向，跨工具打开不窜字符。</summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly string _historyFilePath;
    private readonly ILogger _logger;

    /// <summary>已加载的记录（按追加顺序）；仓库路径键预先算好，避免每次查询重算。</summary>
    private readonly List<HistoryRow> _entries = [];

    /// <param name="historyFilePath">导出历史文件路径，取自 <c>IDataLocation.ExportHistoryFilePath</c>。</param>
    /// <param name="logger">读写问题（坏行、打不开文件）记 Warning 的地方。</param>
    public HistoryService(string historyFilePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyFilePath);
        ArgumentNullException.ThrowIfNull(logger);

        _historyFilePath = historyFilePath;
        _logger = logger;

        Load();
    }

    /// <inheritdoc />
    public void Append(ExportHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.RepositoryPath);

        var row = ToRow(entry);

        lock (_gate)
        {
            _entries.Add(row);
            PersistQuietly(row.Entry);
        }
    }

    /// <inheritdoc />
    public ExportHistoryStatistics GetStatistics()
    {
        lock (_gate)
        {
            // 最常用分支 = 出现次数最多者；并列时取最近一次导出所在的分支
            //（纯按次数并列会取到文件里较早的一条，体感上不如「最近在用的」有用）。
            // 排序是稳定排序，连时间都完全相同的极端情况按文件顺序取先出现者，结果可复现。
            var mostUsedBranch = _entries
                .Where(row => !string.IsNullOrWhiteSpace(row.Entry.BranchName))
                .GroupBy(row => row.Entry.BranchName, StringComparer.Ordinal)
                .Select(group => new
                {
                    Branch = group.Key,
                    Count = group.Count(),
                    LastUsedAt = group.Max(row => row.Entry.ExportedAt),
                })
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.LastUsedAt)
                .FirstOrDefault();

            return new ExportHistoryStatistics(_entries.Count, mostUsedBranch?.Branch);
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLastExportedAt(string repositoryPath)
    {
        var key = RepositoryPathKey.Normalize(repositoryPath);

        lock (_gate)
        {
            DateTimeOffset? latest = null;
            foreach (var row in _entries)
            {
                // 大小写差异由比较器吸收（RepositoryPathKey 只管分隔符与完整路径形式）。
                if (!string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (latest is null || row.Entry.ExportedAt > latest)
                {
                    latest = row.Entry.ExportedAt;
                }
            }

            return latest;
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        lock (_gate)
        {
            _entries.Clear();
            Load();
        }
    }

    /// <inheritdoc />
    public int PruneBefore(DateTimeOffset cutoff)
    {
        lock (_gate)
        {
            var kept = _entries.Where(row => row.Entry.ExportedAt >= cutoff).ToList();
            var removed = _entries.Count - kept.Count;

            if (removed == 0)
            {
                return 0;
            }

            // 先写盘成功再改内存：写盘失败时磁盘仍是真相，内存跟着它（否则首页会少算）。
            try
            {
                var content = string.Concat(
                    kept.Select(row => JsonSerializer.Serialize(row.Entry, SerializerOptions) + "\n"));

                AtomicFile.WriteAllText(_historyFilePath, content);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.Warning(exception, "清理导出历史写盘失败，本次未删除任何记录：{HistoryFilePath}", _historyFilePath);
                return 0;
            }

            _entries.Clear();
            _entries.AddRange(kept);

            _logger.Information(
                "导出历史已按时间清理：删除 {RemovedCount} 条、保留 {KeptCount} 条",
                removed,
                kept.Count);

            return removed;
        }
    }

    /// <summary>
    /// 读取历史文件。文件不存在是正常的（还没导出过）；单行解析失败只跳过该行并记 Warning——
    /// 一行坏数据不该让整个历史消失，更不该让首页失效。
    /// </summary>
    private void Load()
    {
        try
        {
            if (!File.Exists(_historyFilePath))
            {
                return;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(_historyFilePath))
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (TryParse(line, out var row))
                {
                    _entries.Add(row);
                }
                else
                {
                    _logger.Warning(
                        "导出历史第 {LineNumber} 行无法解析，已跳过：{HistoryFilePath}",
                        lineNumber,
                        _historyFilePath);
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 读不到就按「还没有历史」处理：首页显示零统计，导出照常。
            _logger.Warning(
                exception,
                "导出历史文件不可读，本次运行按空历史处理（原文件保留待人工修复）：{HistoryFilePath}",
                _historyFilePath);
        }
    }

    /// <summary>解析一行成内存行；无法解析或缺少仓库路径（无从归属）时返回假。</summary>
    private static bool TryParse(string line, out HistoryRow row)
    {
        row = null!;

        try
        {
            var parsed = JsonSerializer.Deserialize<ExportHistoryEntry>(line, SerializerOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.RepositoryPath))
            {
                return false;
            }

            row = ToRow(parsed);
            return true;
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>追加一行并立即落盘（一条记录 = 一行，以换行结尾，便于继续追加）。</summary>
    private void PersistQuietly(ExportHistoryEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(entry, SerializerOptions) + "\n";
            File.AppendAllText(_historyFilePath, line, Utf8WithoutBom);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 导出已经成功，不能因为记不下历史而让用户以为失败：内存快照保留本次记录
            // （本次会话的首页统计与「上次导出」都看得到），重启后缺失该条，可从日志发现。
            _logger.Warning(
                exception,
                "导出历史写入失败，本次会话内仍计入统计，重启后将缺失该记录：{HistoryFilePath}",
                _historyFilePath);
        }
    }

    /// <summary>
    /// 把一条记录变成内存行：清洗文本字段并算好仓库路径键。写入与读取两条入口共用它，
    /// 保证「刚追加的记录」与「重启后读回的记录」在内存里形态完全一致（查询只看这份形态）。
    /// </summary>
    private static HistoryRow ToRow(ExportHistoryEntry entry)
    {
        var normalized = Normalize(entry);
        return new HistoryRow(normalized, RepositoryPathKey.Normalize(normalized.RepositoryPath));
    }

    /// <summary>
    /// 清洗记录里的文本字段（去首尾空白），保持与写入口径一致。
    /// 这里的 <c>?.</c> 不是多余的防御：记录虽标注为不可空，但历史文件是给人手改的，
    /// 反序列化出的对象可能带 null（缺字段的行就是这种情形）。
    /// </summary>
    private static ExportHistoryEntry Normalize(ExportHistoryEntry entry) => new(
        entry.ExportedAt,
        entry.RepositoryPath.Trim(),
        entry.BranchName?.Trim() ?? string.Empty,
        entry.BaseHash?.Trim() ?? string.Empty,
        entry.HeadHash?.Trim() ?? string.Empty,
        entry.OutputPath?.Trim() ?? string.Empty);

    /// <summary>内存里的一行：原始记录 + 预先算好的仓库路径键。</summary>
    /// <param name="Entry">记录本身。</param>
    /// <param name="Key">仓库路径键（<see cref="RepositoryPathKey.Normalize"/> 的结果）。</param>
    private sealed record HistoryRow(ExportHistoryEntry Entry, string Key);
}
