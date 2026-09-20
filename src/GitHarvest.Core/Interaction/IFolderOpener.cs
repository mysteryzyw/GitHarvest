namespace GitHarvest.Core.Interaction;

/// <summary>
/// 打开目录的服务：让 ViewModel 能响应「打开输出文件夹」这类请求，而不接触任何窗口或进程类型。
/// 接口定义在 Core（与 <see cref="IFolderPicker"/> 同一用途：壳向 ViewModel 提供的交互接缝），
/// 由 WPF 壳用系统文件管理器实现。
/// </summary>
public interface IFolderOpener
{
    /// <summary>
    /// 在系统文件管理器里打开指定目录。
    /// </summary>
    /// <param name="directoryPath">要打开的目录（更新包目录）。</param>
    /// <returns>成功打开返回真；目录不存在或系统拒绝打开时返回假，由调用方给出提示。</returns>
    bool Open(string directoryPath);
}
