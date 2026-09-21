using System.Windows;
using System.Windows.Media;
using GitHarvest.Core.Interaction;
using GitHarvest.Core.Settings;
using GitHarvest.Themes;
using Serilog;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace GitHarvest.Shell;

/// <summary>
/// <see cref="IThemeService"/> 的壳实现：把「浅色 / 深色 / 跟随系统」翻译成 WpfUI 的主题调用，
/// 并让我们自己的主题令牌字典（<c>Themes/Tokens.Light.xaml</c> / <c>Tokens.Dark.xaml</c>）
/// 跟着一起换——WpfUI 只管它自己的画刷，我们那套卡片底色、状态色、块面颜色得自己同步。
///
/// 同步方式：监听 <see cref="ApplicationThemeManager.Changed"/>，在事件里按**实际生效的主题**
/// 换字典。这样「用户直接切档」与「跟随系统时系统主题变化」两条路径收敛到同一处，
/// 不会出现「系统变了主题、我们的卡片底色还是浅色」这种半切换状态。
///
/// 「跟随系统」用 WpfUI 的 <see cref="SystemThemeWatcher"/>：把主窗口交给它盯，
/// 系统主题一变它就重应用（并触发上面的 Changed 事件），不需要我们自己听 Windows 消息。
/// </summary>
internal sealed class ThemeService : IThemeService
{
    /// <summary>原型的强调色（#0067C0）。WpfUI 默认取系统强调色，与原型不符，因此每次换主题后都覆盖回它。</summary>
    private static readonly Color AccentColor = Color.FromRgb(0x00, 0x67, 0xC0);

    private readonly ILogger _logger;

    /// <summary>是否已把主窗口交给 SystemThemeWatcher 盯（避免重复 Watch 与漏 UnWatch）。</summary>
    private bool _watchingSystemTheme;

    private bool _subscribed;

    public ThemeService(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public void Apply(AppThemeOption option)
    {
        try
        {
            SubscribeThemeChanged();

            var followSystem = option == AppThemeOption.FollowSystem;
            UpdateSystemWatch(followSystem);

            // 「跟随系统」取系统当前档位；WpfUI 只有 GetSystemTheme，没有 ApplySystemTheme，
            // 且返回的是 SystemTheme 枚举（与 ApplicationTheme 不同名不同型），要自己映射。
            var theme = option switch
            {
                AppThemeOption.Dark => ApplicationTheme.Dark,
                AppThemeOption.Light => ApplicationTheme.Light,
                _ => MapSystemTheme(ApplicationThemeManager.GetSystemTheme()),
            };

            if (theme is null)
            {
                // 系统处于高对比度等我们没做令牌的档位：按浅色处理，宁可普通也不要一块块空色。
                _logger.Warning(
                    "系统主题 {SystemTheme} 没有对应的深浅令牌，本次按浅色处理。",
                    ApplicationThemeManager.GetSystemTheme());
                theme = ApplicationTheme.Light;
            }

            // updateAccent: false —— 强调色固定为原型色，紧接着由我们显式重算（WpfUI 默认会取系统强调色）。
            ApplicationThemeManager.Apply(theme.Value, WindowBackdropType.Mica, updateAccent: false);
            ApplicationAccentColorManager.Apply(AccentColor, theme.Value);

            // 兜底同步一次令牌字典：Changed 事件在个别路径（如已经是目标主题）可能不触发，
            // 而这里的结果必须是确定的。
            SyncTokensFor(theme.Value);

            _logger.Information("已应用主题：{Option}（实际 {Theme}）", option, theme.Value);
        }
        catch (Exception exception)
        {
            // 主题是外观设置：失败就保持当前外观，绝不因为换了主题把应用弄崩。
            _logger.Warning(exception, "应用主题失败，保持当前外观：{Option}", option);
        }
    }

    /// <summary>把系统主题映射到应用主题；高对比度与未知档位返回 <see langword="null"/>（由调用方决定回退）。</summary>
    private static ApplicationTheme? MapSystemTheme(SystemTheme systemTheme) => systemTheme switch
    {
        SystemTheme.Dark => ApplicationTheme.Dark,
        SystemTheme.Light => ApplicationTheme.Light,
        _ => null,
    };

    /// <summary>
    /// 订阅「WpfUI 主题已变」事件（只订一次）。事件里按实际生效的主题换我们的令牌字典，
    /// 于是「直接切档」与「跟随系统」共用同一条同步逻辑。
    /// </summary>
    private void SubscribeThemeChanged()
    {
        if (_subscribed)
        {
            return;
        }

        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        _subscribed = true;
    }

    private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
        => SyncTokensFor(currentApplicationTheme);

    /// <summary>
    /// 按需开关「跟随系统」：开启时把主窗口交给 SystemThemeWatcher，关闭时收回，
    /// 避免用户切到固定档位后系统主题变化又把界面改回去。
    /// </summary>
    private void UpdateSystemWatch(bool followSystem)
    {
        var window = Application.Current?.MainWindow;

        if (followSystem == _watchingSystemTheme)
        {
            return;
        }

        if (followSystem)
        {
            if (window is null)
            {
                // 组合根会在显示窗口之前应用主题，正常不会走到这里；真到了也只是失去「实时跟随」。
                _logger.Warning("主窗口尚未创建，无法监听系统主题变化；本次仍按系统当前主题着色。");
                return;
            }

            SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, updateAccents: false);
            _watchingSystemTheme = true;
            return;
        }

        if (window is not null)
        {
            SystemThemeWatcher.UnWatch(window);
        }

        _watchingSystemTheme = false;
    }

    /// <summary>
    /// 把主题令牌字典换成与新主题匹配的那一档（真正的替换逻辑在
    /// <see cref="ThemeTokens.Use"/>：离屏渲染核对也走同一条实现）。
    /// 找不到标记字典时它内部会追加一份并返回真，这里补一条日志便于排查。
    /// </summary>
    private void SyncTokensFor(ApplicationTheme theme)
    {
        var dark = theme == ApplicationTheme.Dark;
        if (!ThemeTokens.Use(dark))
        {
            _logger.Warning("应用尚未启动，无法切换主题令牌字典（{Variant}）。", dark ? "Dark" : "Light");
        }
    }
}
