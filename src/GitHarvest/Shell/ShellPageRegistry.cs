using GitHarvest.Core.Navigation;
using GitHarvest.Views;

namespace GitHarvest.Shell;

/// <summary>
/// 导航目的地与实际页面类型的双向对应关系。新增页面时只在这里登记一次。
/// </summary>
internal static class ShellPageRegistry
{
    private static readonly Dictionary<ShellPage, Type> PageTypesByShellPage = new()
    {
        [ShellPage.Repository] = typeof(RepositoryPage),
        [ShellPage.PickCommits] = typeof(PickCommitsPage),
        [ShellPage.Preview] = typeof(PreviewPage),
        [ShellPage.Notes] = typeof(NotesPage),
        [ShellPage.Settings] = typeof(SettingsPage),
        [ShellPage.About] = typeof(AboutPage),
    };

    private static readonly Dictionary<Type, ShellPage> ShellPagesByPageType =
        PageTypesByShellPage.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static Type GetPageType(ShellPage page)
        => PageTypesByShellPage.TryGetValue(page, out var pageType)
            ? pageType
            : throw new ArgumentOutOfRangeException(nameof(page), page, "未知的导航目的地。");

    public static bool TryGetShellPage(Type pageType, out ShellPage page)
        => ShellPagesByPageType.TryGetValue(pageType, out page);
}
