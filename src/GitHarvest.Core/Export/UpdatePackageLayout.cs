namespace GitHarvest.Core.Export;

/// <summary>
/// 更新包的目录布局（CONTEXT.md「更新包」）：更新包位于 <c>输出路径/&lt;更新日期&gt;/</c>，
/// 内含「更新前」「更新后」两个文件夹与「更新说明.md」。
/// 布局只有这一份定义——写出、冲突扫描与界面展示都从这里取，避免三处各写一遍中文文件夹名。
/// </summary>
public static class UpdatePackageLayout
{
    /// <summary>「更新前」文件夹名（存放旧版本快照）。</summary>
    public const string BeforeFolderName = "更新前";

    /// <summary>「更新后」文件夹名（存放新版本快照）。</summary>
    public const string AfterFolderName = "更新后";

    /// <summary>更新说明的文件名。</summary>
    public const string NotesFileName = "更新说明.md";

    /// <summary>指定侧对应的文件夹名。</summary>
    public static string FolderNameFor(SnapshotSide side)
        => side == SnapshotSide.Before ? BeforeFolderName : AfterFolderName;

    /// <summary>指定侧的文件夹完整路径（不含结尾分隔符）。</summary>
    /// <param name="packagePath">更新包根目录（输出路径 + 更新日期目录名）。</param>
    /// <param name="side">「更新前」或「更新后」。</param>
    public static string FolderPath(string packagePath, SnapshotSide side)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        return Path.Combine(packagePath, FolderNameFor(side));
    }

    /// <summary>某个快照文件的完整路径（把 git 的正斜杠相对路径转成系统分隔符）。</summary>
    /// <param name="packagePath">更新包根目录。</param>
    /// <param name="side">「更新前」或「更新后」。</param>
    /// <param name="relativePath">相对仓库根的文件路径（git 风格正斜杠）。</param>
    public static string FullPath(string packagePath, SnapshotSide side, string relativePath)
        => Path.Combine(FolderPath(packagePath, side), ToSystemSeparators(relativePath));

    /// <summary>更新说明的完整路径。</summary>
    /// <param name="packagePath">更新包根目录。</param>
    public static string NotesPath(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        return Path.Combine(packagePath, NotesFileName);
    }

    /// <summary>把 git 输出的正斜杠路径转成当前系统的分隔符（写出与扫描共用同一转换）。</summary>
    /// <param name="relativePath">git 风格相对路径。</param>
    public static string ToSystemSeparators(string relativePath)
        => relativePath.Replace('/', Path.DirectorySeparatorChar);
}
