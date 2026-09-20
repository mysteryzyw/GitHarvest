using System.Text;
using GitHarvest.Core.Settings;
using Serilog;

namespace GitHarvest.Core.Templates;

/// <summary>
/// <see cref="ITemplateService"/> 的实现：自定义模板路径取自全局设置（settings.json 可手改），
/// 读取只认「文件存在且能读」；任何读取失败都回退内置模板——模板是「让说明更好看」的增强，
/// 绝不允许它反过来让导出失败。
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;

    /// <param name="settings">全局设置（默认说明模板路径的来源）。</param>
    /// <param name="logger">模板回退与未知占位符写这里，便于排查。</param>
    public TemplateService(ISettingsService settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TemplateLoadResult> LoadTemplateAsync(CancellationToken cancellationToken = default)
    {
        var templatePath = _settings.Settings.DefaultTemplatePath;
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return new TemplateLoadResult(NotesTemplateCatalog.BuiltInTemplate, IsCustom: false, Notice: null);
        }

        try
        {
            // File.ReadAllText 自动剥 BOM：自定义模板带不带 BOM 都能用。
            var text = await File.ReadAllTextAsync(templatePath, cancellationToken).ConfigureAwait(false);
            return new TemplateLoadResult(text, IsCustom: true, Notice: null);
        }
        // NotSupportedException：手改 settings.json 把路径写成含非法字符的形态（如 a|b.md）时
        // File 抛的是它而不是 IOException——同属「模板读不到」，必须回退而不是让导出失败（用户故事 38）。
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Warning(exception, "自定义说明模板读取失败，回退内置模板：{TemplatePath}", templatePath);
            return new TemplateLoadResult(
                NotesTemplateCatalog.BuiltInTemplate,
                IsCustom: false,
                Notice: $"自定义说明模板读取失败（{templatePath}），本次已回退为内置模板；请检查全局设置中的模板路径。");
        }
    }

    /// <inheritdoc />
    public RenderedNotes Render(string templateText, NotesTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        ArgumentNullException.ThrowIfNull(context);

        var content = new StringBuilder(templateText.Length);
        var warnings = new List<string>();
        var warnedNames = new HashSet<string>(StringComparer.Ordinal);

        // 逐段扫描 {名字}：已知换值、未知原样保留。手写字段里也可能出现花括号
        // （如配置示例），只有跨行花括号不算占位符——名字内不允许换行。
        var position = 0;
        while (position < templateText.Length)
        {
            var open = templateText.IndexOf('{', position);
            if (open < 0)
            {
                content.Append(templateText, position, templateText.Length - position);
                break;
            }

            var close = templateText.IndexOf('}', open + 1);
            if (close < 0)
            {
                // 没有配对的右括号：剩余文本原样收尾。
                content.Append(templateText, position, templateText.Length - position);
                break;
            }

            var name = templateText[(open + 1)..close];
            if (name.Length == 0 || name.Contains('\n') || name.Contains('\r') || name.Contains('{') || !NotesTemplateCatalog.IsKnown(name))
            {
                // 原样保留：左括号前的普通文本 + 这对花括号原文。
                content.Append(templateText, position, close + 1 - position);
                position = close + 1;

                if (name.Length > 0
                    && !name.Contains('\n')
                    && !name.Contains('\r')
                    && !name.Contains('{')
                    && warnedNames.Add(name))
                {
                    warnings.Add($"未知占位符「{{{name}}}」已原样保留——可用占位符共 {NotesTemplateCatalog.Placeholders.Count} 个，请检查是否有笔误。");
                }

                continue;
            }

            content.Append(templateText, position, open - position);
            content.Append(context.ValueOf(name));
            position = close + 1;
        }

        return new RenderedNotes(content.ToString(), warnings);
    }

    /// <summary>「长得像占位符」的判定：非空、不换行、不嵌套左括号（占位符识别的唯一口径）。</summary>
    private static bool IsPlaceholderShape(string name)
        => name.Length > 0 && !name.Contains('\n') && !name.Contains('\r') && !name.Contains('{');
}
