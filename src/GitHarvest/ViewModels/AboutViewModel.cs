using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHarvest.Core.Git;
using GitHarvest.Core.Navigation;

namespace GitHarvest.ViewModels;

/// <summary>
/// 「关于」页的状态：应用版本、git 版本与作者信息（spec 用户故事 47）。
/// 支持沟通时报版本是这一页的全部价值，因此三个值都必须真实：
/// 版本取自程序集，git 版本取自己经探测到的那份结果（复用缓存，不额外启动进程）。
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IGitEnvironmentService _gitEnvironment;

    public AboutViewModel(ShellViewModel shell, IGitEnvironmentService gitEnvironment)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(gitEnvironment);

        Shell = shell;
        _gitEnvironment = gitEnvironment;
    }

    /// <summary>外壳状态（页面标题等）。</summary>
    public ShellViewModel Shell { get; }

    /// <summary>应用版本（程序集版本；发布时由 csproj 的版本属性决定）。</summary>
    public string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";

    /// <summary>作者信息。当前由所有者决定先留占位——仓库里没有任何作者元数据可用。</summary>
    public string Author => "（未设置）";

    /// <summary>运行时描述（原型关于页的小字一行：技术栈）。</summary>
    public string RuntimeDescription => "WPF · .NET 9 · Fluent Design";

    /// <summary>git 版本的展示文案；尚未探测完成时给「检测中…」。</summary>
    [ObservableProperty]
    private string _gitVersionText = "检测中…";

    /// <summary>git 可执行文件路径（支持沟通时确认用的是哪一份 git）；不可用时为空。</summary>
    [ObservableProperty]
    private string _gitExecutablePathText = string.Empty;

    /// <summary>git 不可用时的引导（同一句文案首页也在用，取自自检结果）。</summary>
    [ObservableProperty]
    private string _gitGuidance = string.Empty;

    /// <summary>是否显示 git 不可用的引导条。</summary>
    public bool HasGitGuidance => GitGuidance.Length > 0;

    partial void OnGitGuidanceChanged(string value) => OnPropertyChanged(nameof(HasGitGuidance));

    /// <summary>
    /// 页面加载：取一次 git 自检结果（命中缓存，不重复启动进程）。
    /// 探测失败不抛异常——这一页显示不出 git 版本不影响使用者做别的事。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var status = await _gitEnvironment.GetStatusAsync();

            if (status is { IsAvailable: true })
            {
                GitVersionText = status.Version ?? "未知";
                GitExecutablePathText = status.ExecutablePath ?? string.Empty;
                GitGuidance = string.Empty;
                return;
            }

            GitVersionText = "未检测到 Git";
            GitExecutablePathText = string.Empty;
            GitGuidance = status.Guidance ?? "git 不可用。";
        }
        catch (OperationCanceledException)
        {
            // 页面加载没有取消入口，这是窗口关闭等场景的兜底。
            GitVersionText = "检测被取消";
        }
    }
}
