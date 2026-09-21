using GitHarvest.Core.Settings;

namespace GitHarvest.Core.Interaction;

/// <summary>
/// 应用主题的接缝：让全局设置页能「改了就生效」，而不接触任何 WPF / WpfUI 类型。
/// 与 <see cref="IFolderPicker"/>、<see cref="IFolderOpener"/> 同一用途——接口定义在 Core
/// （守住「ViewModel 只依赖 Core 接口」），由壳用 WpfUI 的主题 API 实现。
/// 主题在启动时也要应用一次（按设置里的默认主题），因此组合根会在显示窗口前调用它。
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// 应用主题并**立即**生效（不重启）：浅色 / 深色 / 跟随系统。
    /// 「跟随系统」还会监听系统主题变化，系统在浅深之间切换时界面跟着变（spec 用户故事 48）。
    /// 应用失败不抛异常——主题是外观设置，失败时保持当前外观并记 Warning 即可。
    /// </summary>
    /// <param name="option">要应用的主题档位（取自全局设置）。</param>
    void Apply(AppThemeOption option);
}
