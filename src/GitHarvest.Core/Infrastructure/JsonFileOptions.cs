using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 应用自有的**给人手改**的 JSON 文件的读写约定：严格 JSON 是给程序看的，
/// 而我们这些文件（settings.json / repository-state.json / location.json）用户会打开编辑，
/// 因此解析宽容（字段名大小写不敏感、允许注释与尾逗号）、中文不转义。
/// 集中在这里，避免每个新文件类型再抄一遍选项——抄漏一个「允许尾逗号」，
/// 用户手改后就会发现「文件读不进去」这种毫无线索的现象。
/// </summary>
internal static class JsonFileOptions
{
    /// <summary>缩进输出 + 宽容解析（人手可读可改的那一类文件）。</summary>
    public static readonly JsonSerializerOptions HumanEditable = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // 默认编码器会把中文转义成 \uXXXX，手改文件就不直观了；这是本应用自有文件而非 Web 报文，允许直接输出。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 枚举写成名字（AppThemeOption 的 Light/Dark/FollowSystem 是 settings.json 的既有契约）；
        // 反过来读时大小写不敏感，手改成 dark 也能认。
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 逐行 JSON（JSONL：导出历史）的选项：宽容解析同上，但**不缩进**——
    /// 一行一条记录，缩进会把文件撑成几十倍大且不再是一行一条。
    /// </summary>
    public static readonly JsonSerializerOptions LineDelimited = new(HumanEditable)
    {
        WriteIndented = false,
    };
}
