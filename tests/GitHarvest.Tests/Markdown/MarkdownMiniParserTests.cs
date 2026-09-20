using GitHarvest.Core.Markdown;

namespace GitHarvest.Tests.Markdown;

/// <summary>
/// 编辑器预览用的 Markdown 迷你解析（ticket 10）：只覆盖更新说明会出现的子集——
/// 三级标题、无序 / 有序列表、段落、表格行（等宽展示）、行内粗体 / 斜体 / 代码。
/// 与原型 renderMd 的分行规则一致；未配对的行内记号按普通文本处理（不吞字）。
/// </summary>
public sealed class MarkdownMiniParserTests
{
    [Fact]
    public void 三级标题各归其位()
    {
        var blocks = MarkdownMiniParser.Parse("# 更新说明\n## 变更统计\n### 新增（1）");

        Assert.Equal(3, blocks.Count);
        var h1 = Assert.IsType<MarkdownHeadingBlock>(blocks[0]);
        var h2 = Assert.IsType<MarkdownHeadingBlock>(blocks[1]);
        var h3 = Assert.IsType<MarkdownHeadingBlock>(blocks[2]);
        Assert.Equal(1, h1.Level);
        Assert.Equal(2, h2.Level);
        Assert.Equal(3, h3.Level);
        Assert.Equal("更新说明", PlainText(h1));
        Assert.Equal("变更统计", PlainText(h2));
        Assert.Equal("新增（1）", PlainText(h3));
    }

    [Fact]
    public void 无序与有序列表识别行首记号()
    {
        var blocks = MarkdownMiniParser.Parse("- 甲\n- 乙\n1. 停止服务\n12. 启动并验证");

        Assert.Equal(4, blocks.Count);
        var first = Assert.IsType<MarkdownListItemBlock>(blocks[0]);
        Assert.Null(first.Number);
        Assert.Equal("甲", PlainText(first));
        var numbered = Assert.IsType<MarkdownListItemBlock>(blocks[3]);
        Assert.Equal(12, numbered.Number);
        Assert.Equal("启动并验证", PlainText(numbered));
    }

    [Fact]
    public void 空行只起分隔作用不产生块()
    {
        var blocks = MarkdownMiniParser.Parse("第一段\n\n\n第二段");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.IsType<MarkdownParagraphBlock>(block));
    }

    [Fact]
    public void 表格行按等宽段落保留原文()
    {
        // 迷你解析不排表格：统计表在预览里以等宽文本呈现，内容一个字符都不少。
        var blocks = MarkdownMiniParser.Parse("| 变更类型 | 文件数 |\n| --- | --- |\n| 新增 | 2 |");

        Assert.Equal(3, blocks.Count);
        var row = Assert.IsType<MarkdownParagraphBlock>(blocks[0]);
        Assert.True(row.IsMonospace);
        Assert.Equal("| 变更类型 | 文件数 |", PlainText(row));
    }

    [Fact]
    public void 行内粗体斜体与代码()
    {
        var blocks = MarkdownMiniParser.Parse("这是**粗体**与*斜体*还有`代码`结尾");

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(Assert.Single(blocks));
        Assert.Collection(
            paragraph.Inlines,
            inline => Assert.Equal(("这是", MarkdownInlineKind.Text), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("粗体", MarkdownInlineKind.Bold), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("与", MarkdownInlineKind.Text), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("斜体", MarkdownInlineKind.Italic), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("还有", MarkdownInlineKind.Text), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("代码", MarkdownInlineKind.Code), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("结尾", MarkdownInlineKind.Text), (inline.Text, inline.Kind)));
    }

    [Fact]
    public void 未配对的行内记号按普通文本保留()
    {
        var blocks = MarkdownMiniParser.Parse("这个**没有闭合\n这个`也没有");

        var first = Assert.IsType<MarkdownParagraphBlock>(blocks[0]);
        Assert.Equal("这个**没有闭合", PlainText(first));
        var second = Assert.IsType<MarkdownParagraphBlock>(blocks[1]);
        Assert.Equal("这个`也没有", PlainText(second));
    }

    [Fact]
    public void 标题行内同样解析行内记号()
    {
        var blocks = MarkdownMiniParser.Parse("## 关于`base..head`的说明");

        var heading = Assert.IsType<MarkdownHeadingBlock>(Assert.Single(blocks));
        Assert.Collection(
            heading.Inlines,
            inline => Assert.Equal(("关于", MarkdownInlineKind.Text), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("base..head", MarkdownInlineKind.Code), (inline.Text, inline.Kind)),
            inline => Assert.Equal(("的说明", MarkdownInlineKind.Text), (inline.Text, inline.Kind)));
    }

    [Fact]
    public void 空文本解析为空清单()
    {
        Assert.Empty(MarkdownMiniParser.Parse(string.Empty));
    }

    /// <summary>把块的内联段拼回纯文本（断言内容用，不关心情粗体等形态）。</summary>
    private static string PlainText(MarkdownBlock block) => string.Concat(block.Inlines.Select(inline => inline.Text));
}
