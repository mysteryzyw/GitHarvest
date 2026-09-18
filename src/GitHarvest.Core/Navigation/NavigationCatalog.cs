namespace GitHarvest.Core.Navigation;

/// <summary>
/// 外壳导航目录的唯一数据源：四步工作流的顺序与中文标题、并列工具页，以及步进语义。
/// 左侧导航、步骤指示条与「上一步 / 下一步」都从这里取标题与顺序，避免多处重复维护。
/// </summary>
public sealed class NavigationCatalog : INavigationCatalog
{
    // 三套文案均按原型：步骤条首尾两步更长（「打开仓库」「更新说明与导出」），
    // 首屏页面标题是欢迎语；未指定的维度回落到导航标题。
    private static readonly NavigationItem[] WorkflowStepItems =
    [
        new(ShellPage.Repository, "仓库", ShellPageKind.WorkflowStep, 1, StepTitle: "打开仓库", PageTitle: "欢迎使用 GitHarvest"),
        new(ShellPage.PickCommits, "选择提交", ShellPageKind.WorkflowStep, 2),
        new(ShellPage.Preview, "导出前总预览", ShellPageKind.WorkflowStep, 3),
        new(ShellPage.Notes, "更新说明", ShellPageKind.WorkflowStep, 4, StepTitle: "更新说明与导出"),
    ];

    private static readonly NavigationItem[] UtilityItems =
    [
        new(ShellPage.Settings, "全局设置", ShellPageKind.Utility, null),
        new(ShellPage.About, "关于", ShellPageKind.Utility, null),
    ];

    private static readonly NavigationItem[] AllItems = [.. WorkflowStepItems, .. UtilityItems];

    private static readonly Dictionary<ShellPage, NavigationItem> ItemsByPage =
        AllItems.ToDictionary(item => item.Page);

    /// <summary>供 XAML 静态绑定使用的默认实例（目录内容不可变，无需 DI）。</summary>
    public static NavigationCatalog Default { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<NavigationItem> WorkflowSteps => WorkflowStepItems;

    /// <inheritdoc />
    public IReadOnlyList<NavigationItem> UtilityPages => UtilityItems;

    /// <inheritdoc />
    public IReadOnlyList<NavigationItem> All => AllItems;

    /// <inheritdoc />
    public NavigationItem Get(ShellPage page)
        => ItemsByPage.TryGetValue(page, out var item)
            ? item
            : throw new ArgumentOutOfRangeException(nameof(page), page, "未知的导航目的地。");

    /// <inheritdoc />
    public NavigationItem? GetNext(ShellPage page)
    {
        var current = Get(page);
        if (!current.IsWorkflowStep)
        {
            return null;
        }

        // 序号从 1 起、数组下标从 0 起，故序号正好等于「下一步」的下标。
        var nextIndex = current.Ordinal!.Value;
        return nextIndex < WorkflowStepItems.Length ? WorkflowStepItems[nextIndex] : null;
    }

    /// <inheritdoc />
    public NavigationItem? GetPrevious(ShellPage page)
    {
        var current = Get(page);
        if (!current.IsWorkflowStep)
        {
            return null;
        }

        // 序号 1 对应下标 0，因此「上一步」的下标是序号 - 2。
        var previousIndex = current.Ordinal!.Value - 2;
        return previousIndex >= 0 ? WorkflowStepItems[previousIndex] : null;
    }

    /// <inheritdoc />
    public bool IsWorkflowStep(ShellPage page) => Get(page).IsWorkflowStep;

    /// <inheritdoc />
    public IReadOnlyList<WorkflowStepStatus> GetStepStatuses(ShellPage current)
    {
        // 工具页不在工作流上，此时没有「当前步骤」，四步一律未到达。
        var currentOrdinal = IsWorkflowStep(current) ? Get(current).Ordinal : null;

        return WorkflowStepItems
            .Select(step => new WorkflowStepStatus(
                step.Ordinal!.Value,
                step.StepTitleOrDefault,
                StatusOf(step.Ordinal!.Value, currentOrdinal)))
            .ToArray();

        static WorkflowStepState StatusOf(int ordinal, int? currentOrdinal) => currentOrdinal switch
        {
            null => WorkflowStepState.Pending,
            var current when ordinal < current => WorkflowStepState.Done,
            var current when ordinal == current => WorkflowStepState.Current,
            _ => WorkflowStepState.Pending,
        };
    }
}
