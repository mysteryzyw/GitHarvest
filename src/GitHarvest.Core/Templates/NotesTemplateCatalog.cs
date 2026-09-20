namespace GitHarvest.Core.Templates;

/// <summary>
/// 更新说明模板的占位符目录与内置模板文本（唯一真相源）：
/// 12 个中文占位符是 spec 用户故事 37 的固定契约——既用于渲染时的「已知占位符」判定，
/// 也用于编辑器工具栏「插入占位符」菜单；内置模板在未配置自定义模板（或自定义模板
/// 读取失败回退）时使用。占位符顺序即菜单顺序，一经发布不宜改动。
/// </summary>
public static class NotesTemplateCatalog
{
    /// <summary>12 个已知占位符（名字不含花括号）。</summary>
    public static IReadOnlyList<TemplatePlaceholder> Placeholders { get; } =
    [
        new("更新日期", "更新日期目录名（默认 yyyy-MM-dd）"),
        new("分支名", "导出时所在的分支"),
        new("基准哈希", "基准提交短哈希"),
        new("基准信息", "基准提交信息首行"),
        new("Head哈希", "Head 提交短哈希"),
        new("Head信息", "Head 提交信息首行"),
        new("新增清单", "新增文件分组（标题 + 条目；为空时整组不渲染）"),
        new("删除清单", "删除文件分组（标题 + 条目；为空时整组不渲染）"),
        new("修改清单", "修改文件分组（标题 + 条目；为空时整组不渲染）"),
        new("重命名清单", "重命名文件分组（旧 → 新与相似度；为空时整组不渲染）"),
        new("其他清单", "其他文件分组（类型变更 / 复制 / 未合并 / 子模块指针）"),
        new("变更统计", "文件总数 + 各变更类型文件数与占比"),
    ];

    /// <summary>判断一个花括号内的名字是否是已知占位符（精确匹配，大小写与空格敏感——这样笔误才能被警告出来）。</summary>
    /// <param name="name">占位符名（不含花括号）。</param>
    /// <returns>是 12 个已知占位符之一时为 <see langword="true"/>。</returns>
    public static bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var placeholder in Placeholders)
        {
            if (string.Equals(placeholder.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 内置模板（Markdown，LF 换行）：标题含更新日期、分支名、基准 / Head 哈希与提交信息，
    /// 变更统计、按变更类型分组的文件清单，以及三个可手写栏目
    /// （更新内容说明 / 操作步骤 / 注意事项，spec 用户故事 36）。
    /// 变更范围行的「共 N 个文件」由 <c>{变更统计}</c> 块给出（占位符契约没有独立的文件总数占位符）。
    /// </summary>
    public static string BuiltInTemplate { get; } = """
        # 更新说明

        - 更新日期：{更新日期}
        - 分支：{分支名}
        - 基准提交：{基准哈希} — {基准信息}
        - Head 提交：{Head哈希} — {Head信息}
        - 变更范围：{基准哈希}..{Head哈希}（含 Head 提交本身、不含基准提交）

        ## 变更统计

        {变更统计}

        ## 更新内容说明

        （请在此填写本次更新的内容说明）

        ## 操作步骤

        （请在此填写现场操作步骤）

        ## 注意事项

        （请在此填写需要特别注意的地方）

        ## 变更文件清单

        「新增」只出现在「更新后」文件夹，「删除」只出现在「更新前」文件夹，其余两类文件夹各一份。
        子模块指针指向的是另一个仓库的提交，没有文件内容，只在下面列出。

        {新增清单}
        {删除清单}
        {修改清单}
        {重命名清单}
        {其他清单}
        """.Replace("\r\n", "\n", StringComparison.Ordinal);
}
