namespace GitHarvest.Core.Export;

/// <summary>Windows 文件系统冲突的类别（spec 用户故事 32）。</summary>
public enum FileSystemConflictKind
{
    /// <summary>路径里含 Windows 不允许的字符（<c>&lt; &gt; : " / \ | ? *</c> 或控制字符）。</summary>
    IllegalCharacter,

    /// <summary>某一层目录名是 Windows 保留设备名（CON / NUL / COM1 …）——Windows 上根本建不出来。</summary>
    ReservedName,

    /// <summary>某一层目录名以点或空格结尾——Windows 会静默去掉，实际路径与预期不一致。</summary>
    TrailingDotOrSpace,

    /// <summary>完整路径达到 Windows 经典长度上限（260 字符），在未开启长路径支持的环境下写不出或解压不开。</summary>
    PathTooLong,

    /// <summary>同一侧里有两个仅大小写不同的同名路径——在大小写不敏感的文件系统上会互相覆盖。</summary>
    CaseCollision,
}

/// <summary>
/// 一条文件系统冲突：类别、涉及的相对路径（大小写冲突涉及两条）与给用户看的中文说明。
/// </summary>
/// <param name="Kind">冲突类别。</param>
/// <param name="RelativePaths">涉及的仓库相对路径（正斜杠风格，重命名前的旧路径按原样列出）。</param>
/// <param name="Description">给用户看的完整说明（含具体字符 / 长度 / 对照路径）。</param>
public sealed record FileSystemConflict(
    FileSystemConflictKind Kind,
    IReadOnlyList<string> RelativePaths,
    string Description);

/// <summary>
/// 导出前的文件系统冲突预扫描（纯函数，不碰文件系统）：把「即将写出的路径」按 Windows 的
/// 文件系统规则检查一遍，发现问题即由编排层中止导出并列出完整清单（spec 用户故事 32：
/// 交付包绝不与仓库不一致——宁可不出包，也不出一个缺文件或互相覆盖的包）。
/// 检查的路径与写出用的是同一份计算（<see cref="UpdatePackageLayout"/>），不会出现「扫过但写歪」。
/// 「更新前」「更新后」是两个独立文件夹，因此大小写冲突只在同一侧内部判定。
/// </summary>
public static class FileSystemConflictScanner
{
    /// <summary>
    /// Windows 经典路径长度上限（MAX_PATH，含结尾空字符）。取经典上限而不是 32767：
    /// 更新包要交给用户的工具链使用，超过它的路径在没开启长路径支持的环境下无法写出或解开。
    /// </summary>
    public const int MaxPathLength = 260;

    /// <summary>按快照计划扫描两侧文件夹，合并成一份完整问题清单（「更新前」的在前）。</summary>
    /// <param name="plan">即将写出的快照计划。</param>
    /// <param name="packagePath">更新包根目录（输出路径 + 更新日期目录名）。</param>
    public static IReadOnlyList<FileSystemConflict> Scan(SnapshotPlan plan, string packagePath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var conflicts = new List<FileSystemConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var side in new[] { SnapshotSide.Before, SnapshotSide.After })
        {
            var paths = plan.Entries
                .Where(entry => entry.Side == side)
                .Select(entry => entry.RelativePath);

            foreach (var conflict in Scan(side, paths, packagePath))
            {
                // 修改类的文件两侧都写，同一条路径的问题会被扫两遍——清单里只说明一次：
                // 名字来自仓库，两侧的修法相同，列两遍只是噪声。
                if (seen.Add($"{conflict.Kind}|{string.Join('|', conflict.RelativePaths)}"))
                {
                    conflicts.Add(conflict);
                }
            }
        }

        return conflicts;
    }

    /// <summary>扫描单个文件夹（某一侧）里即将写出的全部相对路径。</summary>
    /// <param name="side">「更新前」或「更新后」。</param>
    /// <param name="relativePaths">该侧即将写出的相对路径（git 风格正斜杠）。</param>
    /// <param name="packagePath">更新包根目录。</param>
    public static IReadOnlyList<FileSystemConflict> Scan(
        SnapshotSide side,
        IEnumerable<string> relativePaths,
        string packagePath)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var paths = relativePaths as IReadOnlyList<string> ?? [.. relativePaths];
        var conflicts = new List<FileSystemConflict>();

        foreach (var relativePath in paths)
        {
            // 一条路径可能同时踩中多条规则（例如既有非法字符又超长）：全部列出，
            // 用户一次把问题改完，不必反复重试。
            foreach (var conflict in InspectPath(side, relativePath, packagePath))
            {
                conflicts.Add(conflict);
            }
        }

        conflicts.AddRange(FindCaseCollisions(paths));

        return conflicts;
    }

    /// <summary>按 Windows 的文件名 / 路径规则检查一条相对路径。</summary>
    private static IEnumerable<FileSystemConflict> InspectPath(
        SnapshotSide side,
        string relativePath,
        string packagePath)
    {
        // 分隔符在这里是合法的（`/` 就是路径分隔符），所以只看 Win32 的非法字符与控制字符。
        if (WindowsNameRules.TryFindInvalidCharacter(relativePath, treatPathSeparatorsAsInvalid: false, out var invalid))
        {
            yield return new FileSystemConflict(
                FileSystemConflictKind.IllegalCharacter,
                [relativePath],
                $"{relativePath}：路径含 Windows 不允许的字符（{invalid}）。");
        }

        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (WindowsNameRules.EndsWithDotOrSpace(segment))
            {
                yield return new FileSystemConflict(
                    FileSystemConflictKind.TrailingDotOrSpace,
                    [relativePath],
                    $"{relativePath}：目录名或文件名「{segment}」以点或空格结尾，Windows 会静默去掉它，实际产物与预期不一致。");
                break;
            }

            if (WindowsNameRules.IsReservedName(segment))
            {
                yield return new FileSystemConflict(
                    FileSystemConflictKind.ReservedName,
                    [relativePath],
                    $"{relativePath}：名称「{segment}」是 Windows 保留设备名，在 Windows 上无法创建。");
                break;
            }
        }

        var fullPath = UpdatePackageLayout.FullPath(packagePath, side, relativePath);
        if (fullPath.Length >= MaxPathLength)
        {
            yield return new FileSystemConflict(
                FileSystemConflictKind.PathTooLong,
                [relativePath],
                $"{relativePath}：写出后的完整路径长度 {fullPath.Length} 个字符，达到 Windows 的 {MaxPathLength} 字符上限，在没有开启长路径支持的环境下无法写出或解压。");
        }
    }

    /// <summary>找出同一侧里仅大小写不同的同名路径（大小写不敏感的文件系统上会互相覆盖）。</summary>
    private static IEnumerable<FileSystemConflict> FindCaseCollisions(IReadOnlyList<string> relativePaths)
    {
        var groups = relativePaths
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            // 排序让清单顺序稳定（GroupBy 保留首次出现的顺序，组内顺序即输入顺序）。
            var paths = group.Order(StringComparer.Ordinal).ToArray();

            yield return new FileSystemConflict(
                FileSystemConflictKind.CaseCollision,
                paths,
                $"{string.Join(" 与 ", paths)}：仅大小写不同，在大小写不敏感的文件系统上会互相覆盖。");
        }
    }
}
