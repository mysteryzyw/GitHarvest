namespace GitHarvest.Core.Interaction;

/// <summary>
/// 文件夹选择服务：让 ViewModel 能发起「选择文件夹」的界面交互，而不接触任何窗口类型。
/// 接口定义在 Core（保持 ViewModel 只依赖 Core 接口的纪律），由 WPF 壳用文件对话框实现。
/// 这是壳向 ViewModel 提供的交互接缝，不属于 Core 的单元测试接缝（不参与 mock）。
/// </summary>
public interface IFolderPicker
{
    /// <summary>
    /// 显示「选择文件夹」对话框。
    /// </summary>
    /// <param name="initialDirectory">对话框打开时所在的目录；为空时由系统决定。</param>
    /// <returns>用户选中的文件夹完整路径；用户取消时为 <see langword="null"/>。</returns>
    string? PickFolder(string? initialDirectory = null);
}
