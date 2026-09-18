using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GitHarvest.Controls;

/// <summary>
/// 复刻 CSS 网格 auto-fill 语义的流式面板：列数随可用宽度自适应
/// （对应 <c>repeat(auto-fill, minmax(MinItemWidth, 1fr))</c>），列宽把剩余空间均分，
/// 使最后一列的右缘与容器右缘贴齐。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不用 WrapPanel：WrapPanel 的占位槽宽固定，列间距只能靠子项外边距实现，
/// 于是最后一列的右边距也占位——卡片要么被裁窄、要么右缘无法与上方卡片对齐；
/// 而 CSS 网格的 gap 只出现在列之间。原型最近仓库列表
/// （<c>repeat(auto-fill, minmax(330px,1fr))</c>、gap 14）要求两列时各占
/// (容器宽 − gap) / 2 并贴齐容器边缘，必须自定义排布。
/// </para>
/// <para>
/// 行高取该行最高子项，子项默认拉伸到行高（与 CSS 网格的 stretch 一致）；
/// 行距与列距共用 <see cref="Gap"/>（原型的 gap 是一个值）。
/// </para>
/// </remarks>
public sealed class AutoFillPanel : Panel
{
    /// <summary>单列最小宽度：低于它放不下更多列（原型 minmax 下限 330px）。</summary>
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AutoFillPanel),
        new FrameworkPropertyMetadata(330.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>列距 / 行距（原型 .repo-list 的 gap:14px）。</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AutoFillPanel),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>单列最小宽度（原型 minmax 下限 330px），低于它放不下更多列。</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>列距与行距（原型 .repo-list 的 gap:14px，行列同值）。</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>测量与排布的参考宽：宽度不受约束时（防御分支）退化为单列最小宽。</summary>
    private double ResolveContainerWidth(double availableWidth)
        => double.IsInfinity(availableWidth) || availableWidth <= 0
            ? Math.Max(MinItemWidth, 1)
            : availableWidth;

    /// <summary>按容器宽度算列数（CSS auto-fill：能塞下几列算几列，至少一列）。</summary>
    private int ResolveColumnCount(double containerWidth)
    {
        var width = ResolveContainerWidth(containerWidth);
        return Math.Max(1, (int)Math.Floor((width + Gap) / (Math.Max(MinItemWidth, 1) + Gap)));
    }

    /// <summary>列宽：容器宽扣除列间距后均分（1fr 的拉伸语义）。</summary>
    private double ResolveItemWidth(double containerWidth, int columns)
        => Math.Max(1, (ResolveContainerWidth(containerWidth) - Gap * (columns - 1)) / columns);

    /// <summary>
    /// 把子项按列数分行，返回每行高度（行高 = 行内最高子项）。
    /// Measure 阶段传 <paramref name="measure"/> 为 true 顺带按列宽测量子项；
    /// Arrange 阶段子项已量过（列宽一致时结果相同），只读 DesiredSize 分行，不重复测量。
    /// </summary>
    private List<double> SplitIntoRows(int columns, double itemWidth, bool measure)
    {
        var childConstraint = new Size(itemWidth, double.PositiveInfinity);
        var rowHeights = new List<double>();
        var rowHeight = 0d;
        var column = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (measure)
            {
                child.Measure(childConstraint);
            }

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            column++;
            if (column == columns)
            {
                rowHeights.Add(rowHeight);
                rowHeight = 0;
                column = 0;
            }
        }

        // 尾行可能不满列。
        if (column > 0)
        {
            rowHeights.Add(rowHeight);
        }

        return rowHeights;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ResolveColumnCount(availableSize.Width);
        var itemWidth = ResolveItemWidth(availableSize.Width, columns);
        var rowHeights = SplitIntoRows(columns, itemWidth, measure: true);

        // 总高 = 各行高之和 + 行间行距（末行不带行距）。
        var totalHeight = rowHeights.Sum() + Gap * Math.Max(0, rowHeights.Count - 1);
        return new Size(ResolveContainerWidth(availableSize.Width), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ResolveColumnCount(finalSize.Width);
        var itemWidth = ResolveItemWidth(finalSize.Width, columns);
        var rowHeights = SplitIntoRows(columns, itemWidth, measure: false);

        var index = 0;
        var y = 0d;
        foreach (var rowHeight in rowHeights)
        {
            var x = 0d;
            for (var column = 0; column < columns && index < InternalChildren.Count; column++, index++)
            {
                // 子项拉伸到行高（CSS 网格 stretch），列宽由面板统一定。
                InternalChildren[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
                x += itemWidth + Gap;
            }

            y += rowHeight + Gap;
        }

        return finalSize;
    }
}
