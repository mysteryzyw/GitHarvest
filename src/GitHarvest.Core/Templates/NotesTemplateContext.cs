using System.Globalization;
using System.Text;
using GitHarvest.Core.Export;
using GitHarvest.Core.Git;

namespace GitHarvest.Core.Templates;

/// <summary>
/// 渲染一次更新说明所需的全部数据：12 个占位符的取值在此一次算好，
/// 渲染器只做「查名字、换文本」。清单与统计的排版承接 ticket 09 默认内容的格式——
/// 分组块含「### 标题（N）」小节头、空组整块不渲染；统计块给出总数与「类型 / 文件数 / 占比」表。
/// </summary>
public sealed record NotesTemplateContext
{
    /// <summary>清单与统计的分组顺序（五类变更的固定展示顺序）。</summary>
    private static readonly ChangeKind[] OrderedKinds =
    [
        ChangeKind.Added,
        ChangeKind.Deleted,
        ChangeKind.Modified,
        ChangeKind.Renamed,
        ChangeKind.Other,
    ];

    private readonly IReadOnlyDictionary<string, string> _values;

    private NotesTemplateContext(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>按导出请求与变更范围汇总构建上下文（12 个占位符的取值全部预计算）。</summary>
    /// <param name="request">导出请求（更新日期目录名、分支、基准 / Head 的哈希与信息）。</param>
    /// <param name="summary">变更范围的汇总（统计与分组清单都从它取，保证与预览页同一份数字）。</param>
    public static NotesTemplateContext From(ExportRequest request, ChangeRangeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(summary);

        return new NotesTemplateContext(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 更新日期取实际使用的目录名（重名让位成 _HHmmss 后说明与目录一致，ticket 09 沿用语义）。
            ["更新日期"] = request.FolderName,
            ["分支名"] = request.BranchName,
            ["基准哈希"] = request.Base.ShortHash,
            ["基准信息"] = request.Base.Subject,
            ["Head哈希"] = request.Head.ShortHash,
            ["Head信息"] = request.Head.Subject,
            ["新增清单"] = RenderGroup(summary, ChangeKind.Added),
            ["删除清单"] = RenderGroup(summary, ChangeKind.Deleted),
            ["修改清单"] = RenderGroup(summary, ChangeKind.Modified),
            ["重命名清单"] = RenderGroup(summary, ChangeKind.Renamed),
            ["其他清单"] = RenderGroup(summary, ChangeKind.Other),
            ["变更统计"] = RenderStatistics(summary),
        });
    }

    /// <summary>取一个已知占位符的值；调用方须先经 <see cref="NotesTemplateCatalog.IsKnown"/> 判定。</summary>
    /// <param name="name">占位符名（不含花括号）。</param>
    /// <returns>该占位符在当前范围下的渲染文本（清单 / 统计类为多行 Markdown）。</returns>
    public string ValueOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"未知占位符：{name}（渲染前应先用目录判定）。", nameof(name));
    }

    /// <summary>
    /// 一个变更类型的完整分组块：「### 标题（N）」小节头 + 逐条文件条目；
    /// 该组一个文件都没有时渲染为空字符串——空小节整块不出现（与预览页隐藏空组一致）。
    /// </summary>
    private static string RenderGroup(ChangeRangeSummary summary, ChangeKind kind)
    {
        var files = summary.FilesOf(kind);
        if (files.Count == 0)
        {
            return string.Empty;
        }

        var group = new StringBuilder();
        group.Append(CultureInfo.InvariantCulture, $"### {ChangeKindTitles.For(kind)}（{files.Count}）\n\n");
        foreach (var file in files)
        {
            group.Append(CultureInfo.InvariantCulture, $"- {DescribeFile(file)}\n");
        }

        return group.ToString().TrimEnd('\n');
    }

    /// <summary>变更统计：总数行 + 「变更类型 / 文件数 / 占比」表（分组数字的唯一实现来自汇总）。</summary>
    private static string RenderStatistics(ChangeRangeSummary summary)
    {
        var statistics = new StringBuilder();
        statistics.Append(CultureInfo.InvariantCulture,
            $"共 {summary.TotalCount} 个文件（新增 {summary.CountOf(ChangeKind.Added)} / " +
            $"删除 {summary.CountOf(ChangeKind.Deleted)} / 修改 {summary.CountOf(ChangeKind.Modified)} / " +
            $"重命名 {summary.CountOf(ChangeKind.Renamed)} / 其他 {summary.CountOf(ChangeKind.Other)}）\n\n");
        statistics.Append("| 变更类型 | 文件数 | 占比 |\n");
        statistics.Append("| --- | --- | --- |\n");

        foreach (var kind in OrderedKinds)
        {
            statistics.Append(CultureInfo.InvariantCulture,
                $"| {ChangeKindTitles.For(kind)} | {summary.CountOf(kind)} | {summary.PercentOf(kind)}% |\n");
        }

        return statistics.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// 一行文件条目：重命名给出「旧 → 新」与相似度，「其他」给出具体原因，
    /// 二进制（numstat 两侧都以 "-" 占位）标注「二进制」——重命名条目同样标注，与相似度并存（用户故事 29）。
    /// </summary>
    private static string DescribeFile(ChangedFile file)
    {
        // 二进制判定一次算好，重命名与其余条目共用（「其他」显示原因，不参与二进制标注）。
        var binaryNote = file.HasLineCounts ? string.Empty : "（二进制）";

        if (file.Kind == ChangeKind.Renamed && file.OldPath is { } oldPath)
        {
            var similarity = file.SimilarityPercent is { } percent
                ? $"（相似度 {percent.ToString(CultureInfo.InvariantCulture)}%）"
                : string.Empty;

            return $"`{oldPath}` → `{file.Path}`{similarity}{binaryNote}";
        }

        if (file.Kind == ChangeKind.Other)
        {
            return $"`{file.Path}`（{ChangeKindTitles.For(file.OtherReason)}）";
        }

        return $"`{file.Path}`{binaryNote}";
    }
}
