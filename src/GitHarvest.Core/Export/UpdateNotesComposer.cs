using System.Globalization;
using System.Text;
using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>
/// 更新说明的**默认内容**生成（纯函数）：本 ticket 先用代码直接生成，
/// ticket 10 会用 <c>ITemplateService</c> 与 12 个中文占位符取代这里的排版，
/// 届时只需保留同等的要素集合（spec 用户故事 36）：
/// 更新日期、分支名、基准 / Head 哈希与提交信息、变更统计、按变更类型分组的文件清单，
/// 以及三个可手写栏目（更新内容说明 / 操作步骤 / 注意事项）。
/// 说明内容随更新包一同写出为「更新说明.md」，编码由写出方负责（UTF-8 无 BOM，用户故事 41）。
/// </summary>
public static class UpdateNotesComposer
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

    /// <summary>按范围信息与变更清单生成默认的更新说明内容（Markdown，LF 换行）。</summary>
    /// <param name="request">导出请求（更新日期、分支、基准 / Head 的哈希与信息）。</param>
    /// <param name="summary">变更范围的汇总（统计与分组清单都从它取）。</param>
    public static string Compose(ExportRequest request, ChangeRangeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(summary);

        var notes = new StringBuilder();
        notes.Append("# 更新说明\n\n");

        // 范围元信息：哈希与提交信息首行并排，现场核对版本时一眼能对上。
        notes.Append(CultureInfo.InvariantCulture, $"- 更新日期：{request.FolderName}\n");
        notes.Append(CultureInfo.InvariantCulture, $"- 分支：{request.BranchName}\n");
        notes.Append(CultureInfo.InvariantCulture, $"- 基准提交：{request.Base.ShortHash} — {request.Base.Subject}\n");
        notes.Append(CultureInfo.InvariantCulture, $"- Head 提交：{request.Head.ShortHash} — {request.Head.Subject}\n");
        notes.Append(CultureInfo.InvariantCulture, $"- 变更范围：{request.Base.ShortHash}..{request.Head.ShortHash}（共 {summary.TotalCount} 个文件，含 Head 提交本身、不含基准提交）\n\n");

        AppendStatistics(notes, summary);
        AppendPlaceholder(notes, "更新内容说明", "本次更新的内容说明");
        AppendPlaceholder(notes, "操作步骤", "现场操作步骤");
        AppendPlaceholder(notes, "注意事项", "需要特别注意的地方");
        AppendFileList(notes, summary);

        return notes.ToString();
    }

    /// <summary>变更统计：一类一行，文件数与占比都用汇总的数字（分组数字的唯一实现）。</summary>
    private static void AppendStatistics(StringBuilder notes, ChangeRangeSummary summary)
    {
        notes.Append("## 变更统计\n\n");
        notes.Append("| 变更类型 | 文件数 | 占比 |\n");
        notes.Append("| --- | --- | --- |\n");

        foreach (var kind in OrderedKinds)
        {
            notes.Append(CultureInfo.InvariantCulture,
                $"| {ChangeKindTitles.For(kind)} | {summary.CountOf(kind)} | {summary.PercentOf(kind)}% |\n");
        }

        notes.Append('\n');
    }

    /// <summary>一个留白小节：现场交付常有固定套路，给出小标题让使用者直接往里填。</summary>
    private static void AppendPlaceholder(StringBuilder notes, string title, string hint)
        => notes.Append(CultureInfo.InvariantCulture, $"## {title}\n\n（请在此填写{hint}）\n\n");

    /// <summary>按变更类型分组的文件清单；没有文件的分组不出现（与预览页隐藏空组一致）。</summary>
    private static void AppendFileList(StringBuilder notes, ChangeRangeSummary summary)
    {
        notes.Append("## 变更文件清单\n\n");
        notes.Append("「新增」只出现在「更新后」文件夹，「删除」只出现在「更新前」文件夹，其余两类文件夹各一份。\n");
        notes.Append("子模块指针指向的是另一个仓库的提交，没有文件内容，只在下面列出。\n\n");

        foreach (var kind in OrderedKinds)
        {
            var files = summary.FilesOf(kind);
            if (files.Count == 0)
            {
                continue;
            }

            notes.Append(CultureInfo.InvariantCulture, $"### {ChangeKindTitles.For(kind)}（{files.Count}）\n\n");
            foreach (var file in files)
            {
                notes.Append(CultureInfo.InvariantCulture, $"- {DescribeFile(file)}\n");
            }

            notes.Append('\n');
        }
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
