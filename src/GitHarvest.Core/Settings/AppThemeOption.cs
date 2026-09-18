namespace GitHarvest.Core.Settings;

/// <summary>
/// 外观主题选项（全局设置的「默认主题」）。Core 只承载选项本身，
/// WPF 壳负责把它映射到 WpfUI 的主题 API（ticket 12）。
/// JSON 里以枚举名字符串存储（如 <c>FollowSystem</c>），读取时大小写不敏感。
/// </summary>
public enum AppThemeOption
{
    /// <summary>浅色主题（默认值，对齐 ui-fluent 原型的浅色视觉）。</summary>
    Light,

    /// <summary>深色主题。</summary>
    Dark,

    /// <summary>跟随 Windows 系统主题。</summary>
    FollowSystem,
}
