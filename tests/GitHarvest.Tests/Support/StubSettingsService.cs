using GitHarvest.Core.Settings;

namespace GitHarvest.Tests.Support;

/// <summary>
/// <see cref="ISettingsService"/> 的内存桩：不碰磁盘，设置对象直接可读可换。
/// 供「设置只是输入、不是被测对象」的测试使用（如模板服务要读默认说明模板路径、
/// 导出测试要给模板服务一个设置来源）；设置持久化本身的行为由 SettingsServiceTests 覆盖。
/// </summary>
internal sealed class StubSettingsService : ISettingsService
{
    public string GitExecutablePath => Settings.GitExecutablePath;

    public GlobalSettings Settings { get; set; } = new();

    public IReadOnlyList<string> RecentRepositories { get; set; } = [];

    public void Save(GlobalSettings settings) => Settings = settings;

    public RepositoryState GetRepositoryState(string repositoryPath) => new();

    public void SaveRepositoryState(string repositoryPath, RepositoryState state)
    {
    }

    public void AddRecentRepository(string repositoryPath)
    {
    }
}
