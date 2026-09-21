using System.Windows;

namespace GitHarvest.Themes;

/// <summary>
/// 主题令牌字典的切换（浅色 ⇄ 深色）。
///
/// 为什么单独成一个类型：WpfUI 只负责它自己那套画刷，应用自己的令牌
/// （<c>Themes/Tokens.Light.xaml</c> / <c>Tokens.Dark.xaml</c>：卡片底色、状态色、类型五色、
/// 交互叠加色等）必须自己跟着换，而这件事有两处要用——生产路径是
/// <see cref="Shell.ThemeService"/>，离屏渲染核对（<c>.scratch/…/ui-check</c>，无桌面会话时唯一
/// 能看到深色界面的手段）也要用同一条实现，否则核对的是一个「复刻」而不是真实行为。
///
/// 机制：在 <see cref="Application.Resources"/> 的合并字典里，找到带标记键
/// <see cref="VariantKey"/> 的那一个**原地替换**——位置不变，才能继续保持
/// 「排在 WpfUI 字典之后、覆盖同名键」的优先级（App.xaml 里它排在最后）。
/// </summary>
public static class ThemeTokens
{
    /// <summary>主题令牌字典里的标记键：MergedDictionaries 是集合、不能带 x:Key，只能靠内容标记定位。</summary>
    public const string VariantKey = "AppThemeTokensVariant";

    private static readonly Uri LightSource =
        new("pack://application:,,,/GitHarvest;component/Themes/Tokens.Light.xaml");

    private static readonly Uri DarkSource =
        new("pack://application:,,,/GitHarvest;component/Themes/Tokens.Dark.xaml");

    /// <summary>
    /// 把主题令牌换成指定的一档。找不到标记字典时追加一份（宁可多一个字典，
    /// 也不要让界面留着上一档的颜色）。
    /// </summary>
    /// <param name="dark">真为深色档，假为浅色档。</param>
    /// <returns>成功替换或追加返回真；应用还没起来（<see cref="Application.Current"/> 为空）时返回假。</returns>
    public static bool Use(bool dark)
    {
        if (Application.Current is not { } application)
        {
            return false;
        }

        var merged = application.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = dark ? DarkSource : LightSource };

        for (var index = 0; index < merged.Count; index++)
        {
            if (!merged[index].Contains(VariantKey))
            {
                continue;
            }

            merged.RemoveAt(index);
            merged.Insert(index, replacement);
            return true;
        }

        merged.Add(replacement);
        return true;
    }
}
