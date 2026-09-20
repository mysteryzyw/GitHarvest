namespace GitHarvest.Core.Templates;

/// <summary>
/// 模板加载结果：模板文本、是否来自自定义文件，以及回退提示。
/// 自定义模板丢失 / 读取失败时回退内置模板并给出提示（spec 用户故事 38）——
/// 提示文案面向用户，可直接显示在更新说明页。
/// </summary>
/// <param name="Text">模板文本（含占位符，尚未渲染）。</param>
/// <param name="IsCustom">是否来自设置中指定的自定义 .md 模板；未配置或回退时为 <see langword="false"/>。</param>
/// <param name="Notice">回退提示（自定义模板加载失败时非空）；正常加载为 <see langword="null"/>。</param>
public sealed record TemplateLoadResult(string Text, bool IsCustom, string? Notice);
