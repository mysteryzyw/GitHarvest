using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GitHarvest.Core.Markdown;

namespace GitHarvest.Controls;

/// <summary>
/// 更新说明编辑器的预览视图：把 Core 迷你解析器给出的块序列渲染成只读排版
/// （标题 / 段落 / 列表项，行内粗体 / 斜体 / 代码），视觉对齐原型 .md-view——
/// h1 带下边框、列表项左缩进、代码段等宽加浅底。内容随 <see cref="Blocks"/> 整体重建：
/// 预览内容很短（一份更新说明），逐键重建的代价可以忽略。
/// </summary>
public sealed class MarkdownPreviewView : ContentControl
{
    /// <summary>标题三级的字号（原型 .md-view 的 h1/h2 与组小节 h3）。</summary>
    private static readonly double[] HeadingSizes = [19, 15.5, 13.5];

    static MarkdownPreviewView()
    {
        // 预览文字默认跟主题主文字色走；使用处可用 Foreground 覆盖。
        ForegroundProperty.OverrideMetadata(
            typeof(MarkdownPreviewView),
            new FrameworkPropertyMetadata(SystemColors.ControlTextBrush));
    }

    /// <summary>要展示的 Markdown 块序列（Core 迷你解析器的产物）。</summary>
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(MarkdownPreviewView),
        new PropertyMetadata(null, OnBlocksChanged));

    /// <summary>要展示的 Markdown 块序列（Core 迷你解析器的产物）。</summary>
    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    private static void OnBlocksChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((MarkdownPreviewView)sender).Rebuild();

    /// <summary>按当前块序列重建整棵内容树。</summary>
    private void Rebuild()
    {
        var panel = new StackPanel();
        if (Blocks is { } blocks)
        {
            foreach (var block in blocks)
            {
                panel.Children.Add(CreateBlockElement(block));
            }
        }

        Content = panel;
    }

    /// <summary>把一种块建成对应的框架元素（标题包一层 Border 以画 h1 的下边框）。</summary>
    private FrameworkElement CreateBlockElement(MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
            {
                var text = CreateTextBlock(heading.Inlines);
                text.FontSize = HeadingSizes[Math.Clamp(heading.Level, 1, 3) - 1];
                text.FontWeight = FontWeights.SemiBold;
                text.Margin = new Thickness(0, heading.Level == 1 ? 4 : 12, 0, 8);

                if (heading.Level != 1)
                {
                    return text;
                }

                // 原型 .md-view h1：底部细分隔线 + 8px 下内边距。
                return new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = ResolveBrush("AppStrokeBrush", new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4))),
                    Padding = new Thickness(0, 0, 0, 8),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = text,
                };
            }

            case MarkdownListItemBlock item:
            {
                var text = CreateTextBlock(item.Inlines);
                text.Margin = new Thickness(20, 2, 0, 2);
                // 序号 / 圆点作为前置段插到最前（无序 •，有序 N.）；条目为空（「- 」裸行）时直接追加。
                var prefix = new Run(item.Number is { } number ? $"{number}. " : "• ");
                if (text.Inlines.FirstInline is { } first)
                {
                    text.Inlines.InsertBefore(first, prefix);
                }
                else
                {
                    text.Inlines.Add(prefix);
                }

                return text;
            }

            case MarkdownParagraphBlock paragraph:
            {
                var text = CreateTextBlock(paragraph.Inlines);
                text.Margin = new Thickness(0, 4, 0, 4);
                if (paragraph.IsMonospace)
                {
                    // 表格行：不排表格，等宽原样呈现（对齐预览即所得的底线）。
                    text.FontFamily = ResolveMonospaceFont();
                    text.FontSize = 11.5;
                }

                return text;
            }

            default:
                // 迷你解析器的块类型是封闭集合；走到这里说明新增块类型忘了接渲染——显式失败比静默走错分支好查。
                throw new NotSupportedException($"未支持的 Markdown 块类型：{block.GetType().Name}");
        }
    }

    /// <summary>建一个带行内段的 TextBlock：粗体 / 斜体 / 代码（等宽 + 浅底）逐段落到 Run 上。</summary>
    private TextBlock CreateTextBlock(IReadOnlyList<MarkdownInline> inlines)
    {
        var text = new TextBlock
        {
            FontSize = 13,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            // 显式继承控件的 Foreground：Run 的默认前景不随 TextBlock 之外的主题资源走。
            Foreground = Foreground,
        };

        foreach (var inline in inlines)
        {
            var run = new Run(inline.Text);
            switch (inline.Kind)
            {
                case MarkdownInlineKind.Bold:
                    run.FontWeight = FontWeights.Bold;
                    break;
                case MarkdownInlineKind.Italic:
                    run.FontStyle = FontStyles.Italic;
                    break;
                case MarkdownInlineKind.Code:
                    run.FontFamily = ResolveMonospaceFont();
                    run.FontSize = 11.5;
                    run.Background = ResolveBrush("AppTypeOtherSoftBrush", new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
                    break;
            }

            text.Inlines.Add(run);
        }

        return text;
    }

    /// <summary>取主题里的等宽字体（与 MonospaceText 样式同源）；资源缺失时退回 Consolas。</summary>
    private FontFamily ResolveMonospaceFont()
        => TryFindResource("AppMonospaceFontFamily") as FontFamily ?? new FontFamily("Consolas");

    /// <summary>按键取主题画刷；离屏渲染等场景找不到资源时用给定的回退值。</summary>
    private Brush ResolveBrush(string key, Brush fallback)
        => TryFindResource(key) as Brush ?? fallback;
}
