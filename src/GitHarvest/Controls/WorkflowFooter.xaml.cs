using System.Windows;
using System.Windows.Controls;

namespace GitHarvest.Controls;

/// <summary>
/// 工作流页脚：左端是页面注入的本次操作上文（<see cref="LeftContent"/>，如变更范围、
/// 输出路径——与原型 range-bar 的左段一致），右端是上一步 / 下一步。
/// 命令与可用性来自页面级 <c>ShellViewModel</c>（页脚的 DataContext）。
/// </summary>
public partial class WorkflowFooter : UserControl
{
    /// <summary>注册 <see cref="LeftContent"/> 依赖属性。</summary>
    public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
        nameof(LeftContent),
        typeof(object),
        typeof(WorkflowFooter),
        new PropertyMetadata(null));

    public WorkflowFooter() => InitializeComponent();

    /// <summary>左端的上文内容（元素由页面提供，其中绑定用 RelativeSource 指回页面取数据）。</summary>
    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }
}
