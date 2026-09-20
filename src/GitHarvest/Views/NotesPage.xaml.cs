using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 更新说明页（工作流第 4 步）：页面加载时经 Core 的导出预检拿到更新包结构与
/// 文件系统冲突清单（按下导出前就给出反馈）；页脚的「开始导出」由本页 VM 接管。
/// 编辑器的工具栏操作（插入占位符、B/I/代码/列表）是「在光标处动文本」的纯视图行为，
/// 统一在这里完成——改的是 TextBox 的文本与选区，绑定（PropertyChanged 触发）会把
/// 新文本同步回 VM，预览随刷新；VM 不感知光标，守住「VM 只依赖 Core 接口」的边界。
/// 两栏（编辑 / 预览）的滚轮都自留：到边界也不牵连整页滚动（见 <see cref="OnPanePreviewMouseWheel"/>）。
/// </summary>
public partial class NotesPage : Page
{
    private readonly NotesViewModel _viewModel;

    public NotesPage(NotesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面由导航每次新建（Transient），Loaded 只会来一次；显式退订防重复加载。
        Loaded -= OnLoaded;
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 滚轮自留（编辑栏与预览栏共用）：滚轮只滚「鼠标所在的那一栏」，即使该栏已经到顶 / 到底，
    /// 也绝不把这一格滚轮让给外层页面——鼠标停在栏内却把整页滚走是很劣的体感。
    /// 必须自己接管的原因：WPF 的滚动器只在「还能滚」时标记事件已处理，到边界就把事件放行给
    /// 外层页面 ScrollViewer（编辑框模板里的 PART_ContentHost 是同一个族）；这里在隧道路由阶段
    /// （PreviewMouseWheel）先一步完成滚动并置 Handled，内层滚动器与页面滚动器都不会再动。
    /// 两栏都按「行」滚（两栏各自的字号行高生效），一格滚轮滚 3 行，
    /// 跟随系统「一次滚动 N 行」的设置——与 WPF 默认的滚轮手感一致。
    /// </summary>
    private void OnPanePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var lines = Math.Max(1, (int)Math.Round(SystemParameters.WheelScrollLines * (Math.Abs(e.Delta) / 120.0)));
        var up = e.Delta > 0;

        for (var line = 0; line < lines; line++)
        {
            switch (sender)
            {
                case TextBox textBox when up:
                    textBox.LineUp();
                    break;
                case TextBox textBox:
                    textBox.LineDown();
                    break;
                case ScrollViewer viewer when up:
                    viewer.LineUp();
                    break;
                case ScrollViewer viewer:
                    viewer.LineDown();
                    break;
                default:
                    return;
            }
        }

        e.Handled = true;
    }

    /// <summary>「插入占位符」：展开占位符菜单（仅打开菜单，不改变文本）。</summary>
    private void OnPlaceholderButtonClick(object sender, RoutedEventArgs e)
        => PlaceholderPopup.IsOpen = true;

    /// <summary>菜单项：把占位符插到光标处（有选区时替换选区），随后焦点回编辑框。</summary>
    private void OnPlaceholderItemClick(object sender, RoutedEventArgs e)
    {
        PlaceholderPopup.IsOpen = false;
        if (sender is not FrameworkElement { Tag: string name })
        {
            return;
        }

        ReplaceSelection($"{{{name}}}");
        EditorBox.Focus();
    }

    /// <summary>粗体：选区两侧包 **；无选区时插入一对记号并把光标留在中间。</summary>
    private void OnBoldClick(object sender, RoutedEventArgs e) => WrapSelection("**");

    /// <summary>斜体：选区两侧包 *（同粗体的交互）。</summary>
    private void OnItalicClick(object sender, RoutedEventArgs e) => WrapSelection("*");

    /// <summary>行内代码：选区两侧包 `（同粗体的交互）。</summary>
    private void OnCodeClick(object sender, RoutedEventArgs e) => WrapSelection("`");

    /// <summary>
    /// 列表：选区（或光标所在行）的每一行行首加「- 」；已经是列表项的行不重复加。
    /// 多行选区按行处理，与常见 Markdown 编辑器的「转列表」一致。
    /// </summary>
    private void OnListClick(object sender, RoutedEventArgs e)
    {
        var (start, length) = SelectionSpanIncludingFullLines();
        var text = EditorBox.Text;
        var segment = text.Substring(start, length);

        var lines = segment.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? line : "- " + line);
        var replacement = string.Join(Environment.NewLine, lines);

        EditorBox.Text = text.Remove(start, length).Insert(start, replacement);
        EditorBox.Select(start, replacement.Length);
        EditorBox.Focus();
    }

    /// <summary>用一对记号包裹选区；无选区时插入一对空记号、光标落在中间等待输入。</summary>
    private void WrapSelection(string marker)
    {
        var selected = EditorBox.SelectedText;
        if (selected.Length == 0)
        {
            var caret = EditorBox.CaretIndex;
            EditorBox.Text = EditorBox.Text.Insert(caret, marker + marker);
            EditorBox.Select(caret + marker.Length, 0);
        }
        else
        {
            var start = EditorBox.SelectionStart;
            var wrapped = marker + selected + marker;
            EditorBox.Text = EditorBox.Text.Remove(start, selected.Length).Insert(start, wrapped);
            // 包裹后整段选中：连续点同一按钮可以再拆/再包一层之外的记号（如先斜体后粗体）。
            EditorBox.Select(start, wrapped.Length);
        }

        EditorBox.Focus();
    }

    /// <summary>把一段文本写入光标处（替换当前选区），插入后选中刚写入的内容。</summary>
    private void ReplaceSelection(string insertion)
    {
        var start = EditorBox.SelectionStart;
        EditorBox.Text = EditorBox.Text.Remove(start, EditorBox.SelectionLength).Insert(start, insertion);
        EditorBox.Select(start + insertion.Length, 0);
    }

    /// <summary>
    /// 选区扩展到完整行（列表转换按行处理）：无选区时取光标所在行；
    /// 编辑器以 \r\n 换行，行界按 \n 计。
    /// </summary>
    private (int Start, int Length) SelectionSpanIncludingFullLines()
    {
        var text = EditorBox.Text;
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var start = EditorBox.SelectionLength > 0 ? EditorBox.SelectionStart : EditorBox.CaretIndex;
        var end = start + EditorBox.SelectionLength;

        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var lineEnd = text.IndexOf('\n', Math.Min(end, text.Length - 1));
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;

        return (lineStart, lineEnd - lineStart);
    }
}
