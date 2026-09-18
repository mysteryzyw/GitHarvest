using System.Text.Json.Serialization;

namespace GitHarvest.Core.Settings;

/// <summary>
/// 按仓库记住的状态（不可变值对象）：上次输出路径与上次选中的分支。
/// 仓库标识是仓库根目录的完整路径；路径的大小写与分隔符差异（如 <c>D:\Code\Foo</c> 与
/// <c>d:/code/foo</c>）视为同一仓库，与 Windows 文件系统的语义一致。
/// </summary>
public sealed record RepositoryState
{
    /// <summary>该仓库上次使用的输出路径；为空表示尚未记录（导出流程按「本次 &gt; 每仓库 &gt; 全局默认」取值）。</summary>
    [JsonPropertyName("lastOutputPath")]
    public string LastOutputPath { get; init; } = string.Empty;

    /// <summary>该仓库上次选中的分支；为空表示尚未记录（打开仓库时不自动选中）。</summary>
    [JsonPropertyName("lastBranch")]
    public string LastBranch { get; init; } = string.Empty;
}
