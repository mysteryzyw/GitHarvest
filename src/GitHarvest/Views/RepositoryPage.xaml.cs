using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitHarvest.ViewModels;

namespace GitHarvest.Views;

/// <summary>
/// 仓库页（工作流第 1 步）。数据上下文是 <see cref="RepositoryViewModel"/>（外壳状态经它的 Shell 属性取），
/// 页面加载时触发一次 git 环境自检的读取——服务内部缓存结果，因此每次导航回来都不会重新启动进程。
/// 拖放区接受把仓库文件夹拖进来（文件与快捷方式不响应），效果与点击浏览一致。
/// </summary>
public partial class RepositoryPage : Page
{
    private readonly RepositoryViewModel _viewModel;

    public RepositoryPage(RepositoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 命令内部已把异常收敛成「不可用」状态，这里不需要额外的错误处理。
        _viewModel.CheckGitEnvironmentCommand.Execute(null);
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        // 只接受文件夹：拖入文件或快捷方式时不给出可放置反馈，Drop 也就不会触发。
        e.Effects = TryGetDroppedFolder(e.Data) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // 打开中再来一次会并发，直接忽略（打开是毫秒级，用户很快就会回到空闲）。
        if (!_viewModel.IsIdle)
        {
            return;
        }

        if (TryGetDroppedFolder(e.Data) is { } folder)
        {
            OpenDroppedRepository(folder);
        }
    }

    /// <summary>
    /// 取拖入数据里的第一个文件夹；不是文件夹（拖入文件、快捷方式或其他拖入源）时返回
    /// <see langword="null"/>——仓库必须从文件夹进入，文件所在目录交给用户显式选择更不容易误开。
    /// </summary>
    private static string? TryGetDroppedFolder(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return null;
        }

        return Directory.Exists(paths[0]) ? paths[0] : null;
    }

    /// <summary>把拖入的路径回填到输入框并触发与「打开」按钮相同的命令。</summary>
    private void OpenDroppedRepository(string repositoryPath)
    {
        _viewModel.ManualPath = repositoryPath;
        _viewModel.OpenRepositoryCommand.Execute(repositoryPath);
    }
}
