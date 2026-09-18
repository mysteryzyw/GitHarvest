using System.Text.Json.Serialization;

namespace GitHarvest.Core.Settings;

/// <summary>
/// 全局设置的快照（不可变值对象）：默认输出路径、默认说明模板、默认主题与手动指定的 git.exe 路径。
/// 属性名经 <see cref="JsonPropertyNameAttribute"/> 固化为 settings.json 的顶层字段契约——
/// 文件可人手编辑，字段一经发布不得改名（尤其是 <c>gitExecutablePath</c>，git 三级探测依赖它）。
/// 路径类字段的空串与 null 同义，均表示「未配置」。
/// </summary>
public sealed record GlobalSettings
{
    /// <summary>默认输出路径：新仓库首次导出时的默认位置；各仓库记住的上次输出路径优先（spec 用户故事 43）。</summary>
    [JsonPropertyName("defaultOutputPath")]
    public string DefaultOutputPath { get; init; } = string.Empty;

    /// <summary>默认说明模板：自定义 .md 模板文件路径；未配置时使用内置模板（ticket 10 消费）。</summary>
    [JsonPropertyName("defaultTemplatePath")]
    public string DefaultTemplatePath { get; init; } = string.Empty;

    /// <summary>默认主题：浅色 / 深色 / 跟随系统（切换即时生效由 ticket 12 的壳层实现）。</summary>
    [JsonPropertyName("defaultTheme")]
    public AppThemeOption DefaultTheme { get; init; } = AppThemeOption.Light;

    /// <summary>手动指定的 git.exe 路径；未配置时走自动探测（ADR-0001 的三级探测，手动优先级最高）。</summary>
    [JsonPropertyName("gitExecutablePath")]
    public string GitExecutablePath { get; init; } = string.Empty;
}
