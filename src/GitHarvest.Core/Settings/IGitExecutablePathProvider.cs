namespace GitHarvest.Core.Settings;

/// <summary>
/// 全局设置中「手动指定的 git.exe 路径」的读取出口。
/// git 探测只依赖这个窄接口，因此 ticket 03 落地完整的 <c>ISettingsService</c> 后，
/// 只要让它实现本接口并替换 DI 注册，探测逻辑不需要任何改动。
/// </summary>
public interface IGitExecutablePathProvider
{
    /// <summary>手动指定的 git.exe 路径；未配置（或配置不可读）时为 <see langword="null"/>。</summary>
    string? GitExecutablePath { get; }
}
