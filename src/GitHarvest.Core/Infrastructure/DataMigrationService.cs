using Serilog;

namespace GitHarvest.Core.Infrastructure;

/// <summary>数据目录迁移的结论（失败不抛异常，提示随结果走）。</summary>
public sealed record DataMigrationResult
{
    private DataMigrationResult(
        bool isSuccess,
        string targetDirectory,
        int copiedFileCount,
        long copiedBytes,
        string? failureMessage)
    {
        IsSuccess = isSuccess;
        TargetDirectory = targetDirectory;
        CopiedFileCount = copiedFileCount;
        CopiedBytes = copiedBytes;
        FailureMessage = failureMessage;
    }

    /// <summary>是否迁移成功（数据已复制完并且指针已写好）。</summary>
    public bool IsSuccess { get; }

    /// <summary>目标数据目录（失败时也带回用户选的那个路径，便于提示里引用）。</summary>
    public string TargetDirectory { get; }

    /// <summary>复制过去的文件数（失败时为回滚前的数字，仅供参考）。</summary>
    public int CopiedFileCount { get; }

    /// <summary>复制过去的总字节数。</summary>
    public long CopiedBytes { get; }

    /// <summary>失败原因（给用户看的中文提示）；成功时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }

    /// <summary>构造成功的结果。</summary>
    public static DataMigrationResult Succeeded(string targetDirectory, int copiedFileCount, long copiedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        return new(isSuccess: true, targetDirectory, copiedFileCount, copiedBytes, failureMessage: null);
    }

    /// <summary>构造失败的结果。</summary>
    public static DataMigrationResult Failed(string targetDirectory, string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new(isSuccess: false, targetDirectory, 0, 0, failureMessage);
    }
}

/// <summary>
/// 把数据目录迁到别处（设置页「更改目录…」）。语义是**复制**而不是移动：
/// 原目录保留在磁盘上，用户核对无误后自己删——数据迁移是不可逆操作里风险最高的一类，
/// 让用户手里始终留着一份原始数据。
/// </summary>
public interface IDataMigrationService
{
    /// <summary>
    /// 把当前数据目录的内容复制到 <paramref name="targetDirectory"/>，成功后写入指针文件。
    /// 目标必须为空或不存在、且不能与当前数据目录重叠；任何一步失败都会回滚本次复制，
    /// 指针文件不动（仍在原数据目录上运行）。
    /// </summary>
    /// <param name="targetDirectory">新的数据目录（绝对路径）。</param>
    /// <returns>迁移结论；失败不抛异常。</returns>
    DataMigrationResult MigrateTo(string targetDirectory);
}

/// <summary>
/// <see cref="IDataMigrationService"/> 的实现：校验 → 复制（排除指针文件）→ 写指针，失败即回滚。
/// 迁移完成后**不会热切换**：Serilog 的文件 sink 已经绑在旧日志目录上，因此由界面提示重启生效。
/// </summary>
public sealed class DataMigrationService : IDataMigrationService
{
    private readonly IDataLocation _location;
    private readonly ILogger _logger;

    public DataMigrationService(IDataLocation location, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(logger);

        _location = location;
        _logger = logger;
    }

    /// <inheritdoc />
    public DataMigrationResult MigrateTo(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return DataMigrationResult.Failed(string.Empty, "请先选择新的数据目录。");
        }

        string target;
        try
        {
            target = Path.GetFullPath(targetDirectory.Trim());
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DataMigrationResult.Failed(targetDirectory, $"路径无效：{targetDirectory}");
        }

        if (Validate(target) is { } validationFailure)
        {
            return DataMigrationResult.Failed(target, validationFailure);
        }

        var source = _location.DataDirectory;
        var targetExisted = Directory.Exists(target);
        var copiedCount = 0;
        var copiedBytes = 0L;

        try
        {
            Directory.CreateDirectory(target);
            (copiedCount, copiedBytes) = CopyContents(source, target);

            // 复制成功才改指针：指针先改、复制失败的话，应用会指着半个空目录跑。
            _location.SetDataDirectory(target);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException or PathTooLongException)
        {
            _logger.Warning(exception, "数据目录迁移失败，已回滚：{Source} → {Target}", source, target);
            Rollback(target, removeTargetItself: !targetExisted);
            return DataMigrationResult.Failed(target, $"迁移失败：{exception.Message}。已回滚，数据仍在原目录。");
        }

        _logger.Information(
            "数据目录已迁移：{Source} → {Target}（复制 {FileCount} 个文件、{Bytes} 字节；原目录保留）",
            source,
            target,
            copiedCount,
            copiedBytes);

        return DataMigrationResult.Succeeded(target, copiedCount, copiedBytes);
    }

    /// <summary>校验目标路径；通过返回 <see langword="null"/>，否则返回给用户看的原因。</summary>
    private string? Validate(string target)
    {
        var current = _location.DataDirectory;

        if (PathRelation.IsSameOrInside(target, current))
        {
            return "新目录不能是当前数据目录，也不能在它里面（那会把数据复制进自己）。";
        }

        if (PathRelation.IsSameOrInside(current, target))
        {
            return "新目录不能是当前数据目录的上级目录（那会把数据混进上级目录里）。";
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            // 不往有内容的目录里合并：一旦撞名就会覆盖用户的东西，而迁移本来就该是干净的目标。
            return "目标目录不是空的，请选一个空目录或新建一个。";
        }

        return null;
    }

    /// <summary>
    /// 递归复制整个数据目录。**排除指针文件**——它是「位置」信息，不属于用户数据，
    /// 而且它必须留在固定位置（默认数据目录）才有意义。
    /// 源目录不存在（还没产生过任何数据）时视为「没有内容可复制」：
    /// 用户第一次使用就想把数据目录定到别处，不该被当成错误。
    /// </summary>
    private static (int FileCount, long Bytes) CopyContents(string source, string target)
    {
        var fileCount = 0;
        var bytes = 0L;

        if (!Directory.Exists(source))
        {
            return (fileCount, bytes);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (string.Equals(relative, AppPaths.LocationFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);

            fileCount++;
            bytes += new FileInfo(destination).Length;
        }

        return (fileCount, bytes);
    }

    /// <summary>
    /// 回滚本次复制。目标目录在迁移前要么不存在、要么是空的（校验保证），
    /// 因此删掉里面的一切都是删我们自己刚写进去的；目标目录本身只在我们创建它时才删。
    /// </summary>
    private void Rollback(string target, bool removeTargetItself)
    {
        try
        {
            if (!Directory.Exists(target))
            {
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(target))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            if (removeTargetItself && !Directory.EnumerateFileSystemEntries(target).Any())
            {
                Directory.Delete(target);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 回滚本身失败只记 Warning：原始失败才是要给用户的结论。
            _logger.Warning(exception, "迁移失败后回滚不完整，请手动检查目标目录：{Target}", target);
        }
    }
}
