namespace GitHarvest.Core.Templates;

/// <summary>
/// 一个模板占位符的目录项：名字（不含花括号，如「更新日期」）与给使用者看的一句说明。
/// 说明文字同时用于编辑器工具栏的「插入占位符」菜单。
/// </summary>
/// <param name="Name">占位符名（渲染时匹配 <c>{名字}</c> 的完整形态）。</param>
/// <param name="Description">占位符含义的一句话说明。</param>
public sealed record TemplatePlaceholder(string Name, string Description);
