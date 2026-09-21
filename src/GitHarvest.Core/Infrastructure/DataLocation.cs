using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// <see cref="IDataLocation"/> 的实现：构造时读一次指针文件决定数据目录，之后所有派生路径都从它算。
/// 读取宽容：文件不存在（没改过，天天如此）静默用默认目录；损坏、路径非法、路径不存在
/// 都回退默认目录并留下 <see cref="Notice"/>——绝不因为一个坏指针文件让应用起不来。
/// </summary>
public sealed class DataLocation : IDataLocation
{
    private string _dataDirectory;

    /// <summary>用当前环境的漫游应用数据目录（%APPDATA%）构造。</summary>
    public DataLocation()
        : this(AppPaths.GetRoamingAppDataDirectory())
    {
    }

    /// <param name="roamingAppDataDirectory">漫游应用数据根目录（%APPDATA%）；测试可指定临时目录。</param>
    public DataLocation(string roamingAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppDataDirectory);

        DefaultDataDirectory = AppPaths.GetDataDirectory(roamingAppDataDirectory);
        LocationFilePath = AppPaths.GetLocationFilePath(roamingAppDataDirectory);

        var (dataDirectory, notice) = ReadPointerFile();
        _dataDirectory = dataDirectory;
        Notice = notice;
    }

    /// <inheritdoc />
    public string DataDirectory => _dataDirectory;

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(_dataDirectory, AppPaths.LogFolderName);

    /// <inheritdoc />
    public string SettingsFilePath => Path.Combine(_dataDirectory, AppPaths.SettingsFileName);

    /// <inheritdoc />
    public string RepositoryStateFilePath => Path.Combine(_dataDirectory, AppPaths.RepositoryStateFileName);

    /// <inheritdoc />
    public string ExportHistoryFilePath => Path.Combine(_dataDirectory, AppPaths.ExportHistoryFileName);

    /// <inheritdoc />
    public string LocationFilePath { get; }

    /// <inheritdoc />
    public string DefaultDataDirectory { get; }

    /// <inheritdoc />
    public bool IsCustom => !string.Equals(_dataDirectory, DefaultDataDirectory, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? Notice { get; private set; }

    /// <inheritdoc />
    public void SetDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // 归一化：存进指针文件的是完整路径、无结尾分隔符，避免「相对路径随启动目录漂移」
        // 与「同一个目录因尾斜杠被当成两个」这类怪事（规则与仓库路径键共用）。
        var normalized = PathRelation.NormalizeDirectory(dataDirectory);
        var document = new LocationDocument { DataDirectory = normalized };

        AtomicFile.WriteAllText(
            LocationFilePath,
            JsonSerializer.Serialize(document, JsonFileOptions.HumanEditable));

        _dataDirectory = normalized;
        Notice = null;
    }

    /// <summary>
    /// 读指针文件。返回数据目录与容错提示（提示为 <see langword="null"/> 表示无异常）。
    /// 只有「文件存在但内容不可用」才给提示——文件不存在是常态，不该每次都警告。
    /// </summary>
    private (string DataDirectory, string? Notice) ReadPointerFile()
    {
        try
        {
            if (!File.Exists(LocationFilePath))
            {
                return (DefaultDataDirectory, null);
            }

            var document = JsonSerializer.Deserialize<LocationDocument>(
                File.ReadAllText(LocationFilePath),
                JsonFileOptions.HumanEditable);

            var candidate = document?.DataDirectory?.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                return (DefaultDataDirectory, $"数据目录指针文件里没有有效路径，已回退默认目录：{LocationFilePath}");
            }

            if (!Path.IsPathFullyQualified(candidate))
            {
                return (DefaultDataDirectory, $"数据目录指针文件里不是绝对路径（{candidate}），已回退默认目录。");
            }

            var normalized = PathRelation.NormalizeDirectory(candidate);
            if (!Directory.Exists(normalized))
            {
                // 目录被删或挪走了：回退默认目录，但保留指针文件原样，用户自己把目录放回去即可恢复。
                return (DefaultDataDirectory, $"数据目录不存在（{normalized}），已回退默认目录：{DefaultDataDirectory}");
            }

            return (normalized, null);
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
        {
            return (DefaultDataDirectory, $"数据目录指针文件损坏或不可读，已回退默认目录（原文件保留）：{LocationFilePath}（{exception.Message}）");
        }
    }

    /// <summary>指针文件的文件结构（内部契约）：只有一个字段，越简单越不容易被手改坏。</summary>
    private sealed record LocationDocument
    {
        /// <summary>数据目录的绝对路径。</summary>
        [JsonPropertyName("dataDirectory")]
        public string? DataDirectory { get; init; }
    }
}
