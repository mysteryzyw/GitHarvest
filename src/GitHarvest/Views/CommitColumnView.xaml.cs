using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 提交栏（基准 / Head 双栏共用）的代码后置：只承担视图职责——
/// 监听列表滚动位置，滚到底部时通知 ViewModel 追加下一页（增量加载），
/// 并在列表自身滚不动时把滚轮交给页面滚动。
/// </summary>
public sealed partial class CommitColumnView : UserControl
{
    /// <summary>距底部不足这个像素即视为「已滚动到底」。</summary>
    private const double BottomThresholdPixels = 40;

    /// <summary>滚轮一格（Delta=120）折算的页面滚动像素：约三行，贴近系统默认手感。</summary>
    private const double WheelDeltaToPixels = 3;

    public CommitColumnView()
    {
        InitializeComponent();
    }

    private CommitColumnViewModel? ViewModel => DataContext as CommitColumnViewModel;

    private void OnCommitListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ViewModel is not { HasMore: true, IsLoading: false } viewModel)
        {
            return;
        }

        // ScrollChanged 挂在 ListBox 上（附加路由事件，列表内部 ScrollViewer 的事件会冒泡上来）。
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - BottomThresholdPixels)
        {
            if (viewModel.LoadMoreCommand.CanExecute(null))
            {
                viewModel.LoadMoreCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// 滚轮穿透：列表自身已到边界（含列表不满一屏）时，把这一格滚轮交给页面滚动。
    /// WPF 的内层 ScrollViewer 处理滚轮后即标记事件已处理、到边界也不再冒泡，
    /// 用户的体感是「鼠标停在列表上时页面滚不动」——这里显式转发给外层页面滚动器。
    /// </summary>
    private void OnCommitListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindVisualChild<ScrollViewer>(CommitList) is not { } listScroller)
        {
            return;
        }

        var atTop = listScroller.VerticalOffset <= 0 && e.Delta > 0;
        var atBottom = listScroller.VerticalOffset >= listScroller.ScrollableHeight && e.Delta < 0;
        if (!atTop && !atBottom)
        {
            return; // 列表自己还能滚，让默认行为处理
        }

        if (FindParentScrollViewer() is not { } pageScroller)
        {
            return;
        }

        pageScroller.ScrollToVerticalOffset(pageScroller.VerticalOffset - (e.Delta / WheelDeltaToPixels));
        e.Handled = true;
    }

    /// <summary>取页面级的外层滚动器（本控件向上第一个 ScrollViewer）。</summary>
    private ScrollViewer? FindParentScrollViewer()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer scroller)
            {
                return scroller;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
