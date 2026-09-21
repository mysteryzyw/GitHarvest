namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// JSON 文件的原子写入：先把内容写到同目录的临时文件，再替换目标文件。
/// 直接覆写目标文件时，进程在写入中途被终止（断电、被杀）会留下半个 JSON——
/// 下次启动读到损坏文件就只能回退默认值，用户的设置与记忆白丢。
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// 原子写入文本内容（UTF-8 无 BOM）。目录不存在时自动创建；
    /// 同目录的临时文件在成功或失败后都会被清掉。
    /// </summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="content">要写入的完整内容。</param>
    public static void WriteAllText(string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(
            directory ?? string.Empty,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            // File.WriteAllText 默认即 UTF-8 无 BOM。
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
