using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.Navigation;

namespace GitHarvest.ViewModels;

/// <summary>
/// 仓库页（工作流第 1 步）的状态：目前只有 git 环境自检结果与它的引导。
/// 只依赖 Core 的接口——<see cref="IGitEnvironmentService"/> 负责探测，
/// <see cref="IShellNavigator"/> 负责跳转；本类不碰进程、文件系统与界面类型。
/// 仓库本身的业务（打开仓库、最近列表）在 ticket 04 落地。
/// </summary>
public sealed partial class RepositoryViewModel : ObservableObject
{
    private readonly IGitEnvironmentService _gitEnvironment;
    private readonly IShellNavigator _navigator;

    /// <param name="shell">外壳状态，页面标题等仍由它提供。</param>
    /// <param name="gitEnvironment">git 环境自检（结果缓存，重复询问不会反复启动进程）。</param>
    /// <param name="navigator">用于从引导横幅跳到「全局设置」手动指定 git.exe 路径。</param>
    public RepositoryViewModel(
        ShellViewModel shell,
        IGitEnvironmentService gitEnvironment,
        IShellNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(navigator);

        Shell = shell;
        _gitEnvironment = gitEnvironment;
        _navigator = navigator;
    }

    /// <summary>外壳状态（页面标题、步骤指示条等）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>git 环境自检结果；尚未探测时为 <see langword="null"/>。</summary>
    [ObservableProperty]
    private GitEnvironmentStatus? _gitStatus;

    /// <summary>是否要显示「git 不可用」的引导横幅（探测完成且不可用时）。</summary>
    public bool IsGitUnavailable => GitStatus is { IsAvailable: false };

    /// <summary>引导横幅正文（含怎么安装 Git、去哪里手动指定路径）。</summary>
    public string? GitGuidance => GitStatus?.Guidance;

    /// <summary>页面加载时取一次自检结果（首次真正探测，之后直接命中缓存）。</summary>
    [RelayCommand]
    private async Task CheckGitEnvironmentAsync() => GitStatus = await _gitEnvironment.GetStatusAsync();

    /// <summary>「重新检测」：强制重新探测（设置里改过 git.exe 路径后可用它复验）。</summary>
    [RelayCommand]
    private async Task RefreshGitEnvironmentAsync() => GitStatus = await _gitEnvironment.RefreshAsync();

    /// <summary>「前往全局设置」：去手动指定 git.exe 路径（设置页 UI 在 ticket 12）。</summary>
    [RelayCommand]
    private void OpenSettings() => _navigator.NavigateTo(ShellPage.Settings);

    partial void OnGitStatusChanged(GitEnvironmentStatus? value)
    {
        OnPropertyChanged(nameof(IsGitUnavailable));
        OnPropertyChanged(nameof(GitGuidance));
    }
}
