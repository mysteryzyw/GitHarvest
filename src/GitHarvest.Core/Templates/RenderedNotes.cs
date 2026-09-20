namespace GitHarvest.Core.Templates;

/// <summary>
/// 一次占位符渲染的结果：渲染后的说明文本与警告列表。
/// 未知占位符在文本中原样保留（spec 用户故事 39），并各产生一条警告——
/// 警告按占位符名去重，同一笔误出现多次只提醒一次。
/// </summary>
/// <param name="Content">渲染后的说明文本（已知占位符已替换，未知占位符原样保留）。</param>
/// <param name="Warnings">未知占位符的警告（每条含占位符名）；全部为已知时为空列表。</param>
public sealed record RenderedNotes(string Content, IReadOnlyList<string> Warnings);
