using System.Diagnostics;
using System.IO;
using GitHarvest.Core.Interaction;
using Serilog;

namespace GitHarvest.Shell;

/// <summary>
/// 用系统文件管理器打开目录（壳侧实现）：更新说明页的「打开输出文件夹」走这里。
/// 目录不存在或系统拒绝打开时返回假并记日志——不抛异常，页面会给出「请手动前往」的提示。
/// </summary>
internal sealed class FolderOpener : IFolderOpener
{
    private readonly ILogger _logger;

    public FolderOpener(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public bool Open(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.Warning("要打开的目录不存在：{DirectoryPath}", directoryPath);
            return false;
        }

        try
        {
            // UseShellExecute 交给系统决定用什么程序打开：与在资源管理器里双击目录一致。
            Process.Start(new ProcessStartInfo { FileName = directoryPath, UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Warning(exception, "打开目录失败：{DirectoryPath}", directoryPath);
            return false;
        }
    }
}
