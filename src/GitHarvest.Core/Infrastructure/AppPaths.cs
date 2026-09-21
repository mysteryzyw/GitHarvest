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

    /// <summary>导出历史文件名（JSONL：一行一条记录，只追加）。</summary>
    public const string ExportHistoryFileName = "export-history.jsonl";

    /// <summary>
    /// 数据目录指针文件名。它**固定在默认数据目录下**（不随自定义数据目录迁移），
    /// 是「数据目录被改到哪儿」的唯一真相源——把位置配置放进被配置的目录里会形成自指循环。
    /// </summary>
    public const string LocationFileName = "location.json";

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

    /// <summary>导出历史文件：%APPDATA%\GitHarvest\export-history.jsonl。</summary>
    public static string GetExportHistoryFilePath() => GetExportHistoryFilePath(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析导出历史文件。</summary>
    public static string GetExportHistoryFilePath(string roamingAppDataDirectory)
        => Path.Combine(GetDataDirectory(roamingAppDataDirectory), ExportHistoryFileName);

    /// <summary>数据目录指针文件（固定位置，永远在默认数据目录下）：%APPDATA%\GitHarvest\location.json。</summary>
    public static string GetLocationFilePath() => GetLocationFilePath(ResolveRoamingAppDataDirectory());

    /// <summary>在指定漫游应用数据根目录下解析数据目录指针文件。</summary>
    public static string GetLocationFilePath(string roamingAppDataDirectory)
        => Path.Combine(GetDataDirectory(roamingAppDataDirectory), LocationFileName);

    /// <summary>漫游应用数据根目录（%APPDATA%）：<see cref="DataLocation"/> 解析自定义数据目录时要用它。</summary>
    public static string GetRoamingAppDataDirectory() => ResolveRoamingAppDataDirectory();

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
