namespace GitHarvest.Core.Markdown;

/// <summary>一个块级元素（标题 / 段落 / 列表项）的基形态：都携带一串行内段。</summary>
/// <param name="Inlines">块内的行内段（至少一段；空行不会产生块）。</param>
public abstract record MarkdownBlock(IReadOnlyList<MarkdownInline> Inlines);

/// <summary>标题块（# / ## / ###，更新说明最多用到三级）。</summary>
/// <param name="Level">标题级别（1～3）。</param>
/// <param name="Inlines">标题文字的行内段。</param>
public sealed record MarkdownHeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines)
    : MarkdownBlock(Inlines);

/// <summary>段落块；表格行（| 开头）不排版成表格，按等宽段落原样展示。</summary>
/// <param name="IsMonospace">是否用等宽字体展示（表格行）。</param>
/// <param name="Inlines">段落文字的行内段。</param>
public sealed record MarkdownParagraphBlock(bool IsMonospace, IReadOnlyList<MarkdownInline> Inlines)
    : MarkdownBlock(Inlines);

/// <summary>列表项块（「- 」无序或「N. 」有序）。</summary>
/// <param name="Number">有序列表的序号；无序列表为 <see langword="null"/>。</param>
/// <param name="Inlines">条目文字的行内段。</param>
public sealed record MarkdownListItemBlock(int? Number, IReadOnlyList<MarkdownInline> Inlines)
    : MarkdownBlock(Inlines);
