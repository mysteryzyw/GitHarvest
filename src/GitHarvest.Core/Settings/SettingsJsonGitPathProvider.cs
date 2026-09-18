using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace GitHarvest.Core.Settings;

/// <summary>
/// 从 settings.json 里读取手动指定的 git.exe 路径（只读，写入与其余设置项由 ticket 03 的
/// <c>ISettingsService</c> 负责）。
/// 设置文件是可手工编辑的，因此这里格外宽容：文件不存在、根不是对象、字段缺失、值为空白，
/// 一律按「未配置」处理；解析失败记 Warning 后同样按「未配置」处理，绝不抛出——
/// 探测不该因为设置文件坏了而让应用起不来。
/// </summary>
public sealed class SettingsJsonGitPathProvider : IGitExecutablePathProvider
{
    /// <summary>手动指定 git.exe 路径的设置项名（settings.json 中的顶层字段）。</summary>
    public const string PropertyName = "gitExecutablePath";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // 设置文件可人手编辑：字段名大小写、注释与尾逗号都不该成为障碍。
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _settingsFilePath;
    private readonly ILogger _logger;

    /// <param name="settingsFilePath">settings.json 的路径，通常取自 <c>AppPaths.GetSettingsFilePath()</c>。</param>
    /// <param name="logger">探测与自检日志。</param>
    public SettingsJsonGitPathProvider(string settingsFilePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsFilePath = settingsFilePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? GitExecutablePath
    {
        get
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    // 首次启动还没有设置文件，属正常情况，无需打扰用户。
                    return null;
                }

                var document = JsonSerializer.Deserialize<SettingsDocument>(
                    File.ReadAllText(_settingsFilePath),
                    SerializerOptions);

                var value = document?.GitExecutablePath;
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.Warning(
                    exception,
                    "读取全局设置中的 git.exe 路径失败，按未配置处理：{SettingsFilePath}",
                    _settingsFilePath);

                return null;
            }
        }
    }

    /// <summary>settings.json 中与 git 路径相关的字段（其余设置项由 ticket 03 接管）。</summary>
    private sealed record SettingsDocument(
        [property: JsonPropertyName(PropertyName)] string? GitExecutablePath);
}
