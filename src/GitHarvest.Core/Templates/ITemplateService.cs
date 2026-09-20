namespace GitHarvest.Core.Templates;

/// <summary>
/// 更新说明模板服务（spec 模块边界中的 <c>ITemplateService</c>）：
/// 模板加载（自定义失败回退内置并提示）与 12 个中文占位符渲染（未知占位符原样保留并警告）。
/// 渲染是纯函数，加载是唯一碰文件系统的地方（自定义 .md 模板）；自定义模板的路径来自全局设置
/// （<c>defaultTemplatePath</c>，可手改 settings.json 指定）。
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// 加载当前生效的模板：未配置自定义模板时用内置模板；自定义模板丢失 / 读取失败时
    /// 回退内置模板并在结果里带上给用户看的提示（绝不让模板问题导致导出失败，用户故事 38）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板文本、来源（内置 / 自定义）与回退提示。</returns>
    Task<TemplateLoadResult> LoadTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 渲染一段含占位符的文本：12 个已知占位符替换为当前范围的值；
    /// 未知占位符原样保留并各记一条警告（按占位符名去重，用户故事 39）。
    /// 编辑器的实时预览与最终写出更新说明走同一个渲染入口，所见即所得。
    /// </summary>
    /// <param name="templateText">含占位符的文本（模板或用户编辑后的说明内容）。</param>
    /// <param name="context">当前范围的数据（12 个占位符的取值）。</param>
    RenderedNotes Render(string templateText, NotesTemplateContext context);
}
