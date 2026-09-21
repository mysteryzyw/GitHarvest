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

        return StartProcess(directoryPath, arguments: null, "目录");
    }

    /// <inheritdoc />
    public bool RevealFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.Warning("要定位的文件不存在：{FilePath}", filePath);
            return false;
        }

        // 交给资源管理器选中该文件：/select 需要一个已存在的路径，上面已经确认过。
        return StartProcess("explorer.exe", $"/select,\"{filePath}\"", "文件");
    }

    /// <summary>启动一个交给系统处理的目标；失败记 Warning 并返回假（不抛异常）。</summary>
    private bool StartProcess(string fileName, string? arguments, string description)
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = true };
            if (arguments is not null)
            {
                startInfo.Arguments = arguments;
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Warning(exception, "打开{Description}失败：{Target}", description, fileName);
            return false;
        }
    }
}
