using System.Text;

namespace GitHarvest.Core.Markdown;

/// <summary>
/// 编辑器预览用的 Markdown 迷你解析（纯函数）：只认更新说明会出现的子集——
/// 三级标题、无序 / 有序列表、段落、表格行（等宽展示）与行内粗体 / 斜体 / 代码。
/// 分行规则与原型 renderMd 一致；解析全程宽容：未配对的记号按普通文本保留，
/// 绝不因为用户写到一半的标记而吞字或抛异常（实时预览逐键触发）。
/// </summary>
public static class MarkdownMiniParser
{
    /// <summary>把说明文本解析成块序列；空行只起分隔作用，不产生块。</summary>
    /// <param name="markdown">说明文本（\n 或 \r\n 换行均可——编辑器给出的是 \r\n）。</param>
    /// <returns>块序列（原文为空时为空白单）。</returns>
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var blocks = new List<MarkdownBlock>();
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(new MarkdownHeadingBlock(3, ParseInlines(line[4..])));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                blocks.Add(new MarkdownHeadingBlock(2, ParseInlines(line[3..])));
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                blocks.Add(new MarkdownHeadingBlock(1, ParseInlines(line[2..])));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                blocks.Add(new MarkdownListItemBlock(Number: null, ParseInlines(line[2..])));
            }
            else if (TrySplitOrderedItem(line, out var number, out var orderedText))
            {
                blocks.Add(new MarkdownListItemBlock(number, ParseInlines(orderedText)));
            }
            else if (line.StartsWith('|'))
            {
                // 迷你解析不排表格：统计表在预览里以等宽文本呈现，内容一个字符都不少。
                blocks.Add(new MarkdownParagraphBlock(IsMonospace: true, ParseInlines(line)));
            }
            else
            {
                blocks.Add(new MarkdownParagraphBlock(IsMonospace: false, ParseInlines(line)));
            }
        }

        return blocks;
    }

    /// <summary>
    /// 解析一段文字的行内段：<c>`代码`</c> 优先（代码里不再认其他记号），
    /// 其次 <c>**粗体**</c>，最后 <c>*斜体*</c>；找不到配对的闭合记号时按普通文本保留。
    /// </summary>
    private static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var inlines = new List<MarkdownInline>();
        var plain = new StringBuilder();

        var position = 0;
        while (position < text.Length)
        {
            // 依次尝试三种记号；任一命中即产出一段，未命中把当前字符并入普通段。
            if (TryTakeSpan(text, position, "`", MarkdownInlineKind.Code, inlines, plain, out position)
                || TryTakeSpan(text, position, "**", MarkdownInlineKind.Bold, inlines, plain, out position)
                || TryTakeSpan(text, position, "*", MarkdownInlineKind.Italic, inlines, plain, out position))
            {
                continue;
            }

            plain.Append(text[position]);
            position++;
        }

        FlushPlain(inlines, plain);
        return inlines;
    }

    /// <summary>尝试在 <paramref name="start"/> 处取一段标记文本；闭合记号缺失时返回 <see langword="false"/>（按普通文本处理）。</summary>
    private static bool TryTakeSpan(
        string text,
        int start,
        string marker,
        MarkdownInlineKind kind,
        List<MarkdownInline> inlines,
        StringBuilder plain,
        out int next)
    {
        next = start;
        if (!text.AsSpan(start).StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        var close = text.IndexOf(marker, start + marker.Length, StringComparison.Ordinal);
        if (close < 0 || close == start + marker.Length)
        {
            // 没有闭合记号（或记号内是空的）：不当标记，调用方按普通文本逐字收下。
            return false;
        }

        FlushPlain(inlines, plain);
        inlines.Add(new MarkdownInline(kind, text[(start + marker.Length)..close]));
        next = close + marker.Length;
        return true;
    }

    /// <summary>把攒着的普通文本落成一段（有内容才落）。</summary>
    private static void FlushPlain(List<MarkdownInline> inlines, StringBuilder plain)
    {
        if (plain.Length == 0)
        {
            return;
        }

        inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, plain.ToString()));
        plain.Clear();
    }

    /// <summary>识别「N. 」开头的有序列表项（N 为任意位数的正整数）。</summary>
    private static bool TrySplitOrderedItem(string line, out int number, out string text)
    {
        number = 0;
        text = string.Empty;

        var dot = line.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot + 1 >= line.Length || line[dot + 1] != ' ')
        {
            return false;
        }

        if (!int.TryParse(line.AsSpan(0, dot), out number))
        {
            return false;
        }

        text = line[(dot + 2)..];
        return true;
    }
}
