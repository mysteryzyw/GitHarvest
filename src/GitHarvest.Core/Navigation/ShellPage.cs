namespace GitHarvest.Core.Navigation;

/// <summary>
/// 应用外壳中的导航目的地。前四项构成四步工作流，后两项是并列的工具页。
/// 术语以 CONTEXT.md 为准（导出前总预览、更新说明、全局设置）。
/// </summary>
public enum ShellPage
{
    /// <summary>仓库：打开本地 Git 仓库。</summary>
    Repository = 1,

    /// <summary>选择提交：选定基准提交与 Head 提交。</summary>
    PickCommits = 2,

    /// <summary>导出前总预览：按变更类型确认变更范围。</summary>
    Preview = 3,

    /// <summary>更新说明：编辑本次交付的说明并导出。</summary>
    Notes = 4,

    /// <summary>全局设置（工具页）。</summary>
    Settings = 5,

    /// <summary>关于（工具页）。</summary>
    About = 6,
}
