namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 应用数据与日志目录的位置约定：%APPDATA%\GitHarvest\（设置、日志、导出历史都在其下）。
/// 纯路径计算，不创建目录、不触碰磁盘。
/// </summary>
public static class AppPaths
{
    /// <summary>漫游应用数据下的产品目录名。</summary>
    public const string ProductFolderName = "GitHarvest";

    /// <summary>数据目录下的日志子目录名。</summary>
    public const string LogFolderName = "logs";

    /// <summary>全局设置文件名。</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>每仓库状态文件名（上次输出路径/上次分支/最近仓库列表）。</summary>
    public const string RepositoryStateFileName = "repository-state.json";

    /// <summary>数据目录：%APPDATA%\GitHarvest。</summary>
    public static string GetDataDirectory() => GetDataDirectory(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析数据目录。</summary>
    public static string GetDataDirectory(string roamingAppDataDirectory)
        => Path.Combine(Normalize(roamingAppDataDirectory), ProductFolderName);

    /// <summary>全局设置文件：%APPDATA%\GitHarvest\settings.json。</summary>
    public static string GetSettingsFilePath() => GetSettingsFilePath(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析全局设置文件。</summary>
    public static string GetSettingsFilePath(string roamingAppDataDirectory)
        => Path.Combine(GetDataDirectory(roamingAppDataDirectory), SettingsFileName);

    /// <summary>每仓库状态文件：%APPDATA%\GitHarvest\repository-state.json。</summary>
    public static string GetRepositoryStateFilePath() => GetRepositoryStateFilePath(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析每仓库状态文件。</summary>
    public static string GetRepositoryStateFilePath(string roamingAppDataDirectory)
        => Path.Combine(GetDataDirectory(roamingAppDataDirectory), RepositoryStateFileName);

    /// <summary>日志目录：%APPDATA%\GitHarvest\logs。</summary>
    public static string GetLogDirectory() => GetLogDirectory(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析日志目录。</summary>
    public static string GetLogDirectory(string roamingAppDataDirectory)
        => Path.Combine(GetDataDirectory(roamingAppDataDirectory), LogFolderName);

    private static string Normalize(string roamingAppDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(roamingAppDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppDataDirectory);

        return roamingAppDataDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string ResolveRoamingAppDataDirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roaming))
        {
            return roaming;
        }

        // 极少数环境下 %APPDATA% 缺失，退回到用户配置文件下的约定位置。
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "AppData", "Roaming");
        }

        throw new InvalidOperationException("无法确定漫游应用数据目录（%APPDATA%）。");
    }
}
