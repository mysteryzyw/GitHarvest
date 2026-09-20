namespace GitHarvest.Core.Markdown;

/// <summary>行内段的形态（普通文本 / 粗体 / 斜体 / 代码）。</summary>
public enum MarkdownInlineKind
{
    /// <summary>普通文本。</summary>
    Text,

    /// <summary>粗体（<c>**文字**</c>）。</summary>
    Bold,

    /// <summary>斜体（<c>*文字*</c>）。</summary>
    Italic,

    /// <summary>行内代码（<c>`代码`</c>）。</summary>
    Code,
}

/// <summary>
/// 一段行内内容：文本 + 形态。迷你解析不嵌套（粗体里不再解析斜体），
/// 与原型 renderMd 的能力对齐——更新说明用不到嵌套标记。
/// </summary>
/// <param name="Kind">段形态。</param>
/// <param name="Text">段文本（已剥掉标记符号）。</param>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text);
