using System.Globalization;

namespace GitHarvest.Core.Git;

/// <summary>
/// git 差异输出（<c>-z</c> 空字符分隔格式）的解析与变更类型归类：
/// 把 <c>git diff --raw -M -z</c>、<c>git diff --numstat -M -z</c> 与
/// <c>git ls-tree -r -l</c> 三份输出合成为 <see cref="ChangedFile"/> 清单。
/// 解析是纯函数（不碰进程与文件系统），异常形态返回 <see langword="null"/> 让调用方
/// 按 Git 错误处理——与提交列表的解析容错同一口径。
/// 分类依据（<c>git diff --raw</c> 状态字母）：
/// A→新增、D→删除、M→修改、R→重命名（相似度随记录携带，阈值由 git -M 的默认 50% 决定）、
/// C/T/U/X→其他（复制 / 类型变更 / 未合并 / 无法识别）；
/// gitlink（模式 160000）优先于 A/D/M 判为「其他·子模块指针」——见 <see cref="Classify"/>。
/// </summary>
public static class ChangeRangeParser
{
    /// <summary>子模块（gitlink）在 git 模式字段里的取值：内容不在本仓库，只有指向的提交哈希。</summary>
    public const string GitlinkMode = "160000";

    /// <summary>全零 blob 哈希：git 用它占位「该侧没有此文件」（新增的旧侧 / 删除的新侧）。</summary>
    private const string NullBlobHash = "0000000000000000000000000000000000000000";

    /// <summary>
    /// 解析 <c>git diff --raw -M -z --no-abbrev</c> 的输出。
    /// 记录形如 <c>:旧模式 新模式 旧blob 新blob 状态\0路径[\0新路径]\0</c>；
    /// 重命名 / 复制带两个路径。无法解析时返回 <see langword="null"/>。
    /// </summary>
    public static List<RawChangeRecord>? ParseRaw(string output)
    {
        var records = new List<RawChangeRecord>();
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;

        while (index < fields.Length)
        {
            var header = fields[index++];
            if (!header.StartsWith(':'))
            {
                return null;
            }

            var headerFields = header[1..].Split(' ');
            if (headerFields.Length != 5)
            {
                return null;
            }

            var (oldMode, newMode, oldBlob, newBlob, status) = (headerFields[0], headerFields[1], headerFields[2], headerFields[3], headerFields[4]);

            // 状态字母后可带相似度分数（R100 / C80）；分数必须是纯数字。
            var letter = status.Length > 0 ? status[..1] : string.Empty;
            int? score = null;
            if (status.Length > 1)
            {
                if (!int.TryParse(status[1..], out var parsedScore))
                {
                    return null;
                }

                score = parsedScore;
            }

            // 路径字段：重命名 / 复制带旧新两个路径，其余一个。字段不足按输出异常处理。
            if (letter.Length == 0 || index >= fields.Length)
            {
                return null;
            }

            var path = fields[index++];
            string? newPath = null;
            if (letter is "R" or "C")
            {
                if (index >= fields.Length)
                {
                    return null;
                }

                newPath = fields[index++];
            }

            records.Add(new RawChangeRecord(
                oldMode,
                newMode,
                oldBlob == NullBlobHash ? null : oldBlob,
                newBlob == NullBlobHash ? null : newBlob,
                letter,
                score,
                path,
                newPath));
        }

        return records;
    }

    /// <summary>
    /// 解析 <c>git diff --numstat -M -z</c> 的输出。
    /// 实测记录形态：普通为 <c>增\t删\t路径\0</c>（数字与路径之间是制表符），
    /// 重命名 / 复制为 <c>增\t删\t\0旧路径\0新路径\0</c>（数字后直接 NUL，旧新路径随后）；
    /// 二进制文件增删为 "-"（→ <see langword="null"/>）。无法解析时返回 <see langword="null"/>。
    /// </summary>
    public static List<NumstatRecord>? ParseNumstat(string output)
    {
        var records = new List<NumstatRecord>();
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;

        while (index < fields.Length)
        {
            // 第一个字段是「增\t删\t[路径]」：数字与路径之间制表符分隔，重命名记录的路径位为空。
            var parts = fields[index++].Split('\t');
            if (parts.Length != 3)
            {
                return null;
            }

            var additions = ParseLineCount(parts[0]);
            var deletions = ParseLineCount(parts[1]);
            if ((additions is null && parts[0] != "-") || (deletions is null && parts[1] != "-"))
            {
                return null;
            }

            if (parts[2].Length > 0)
            {
                records.Add(new NumstatRecord(additions, deletions, parts[2], null));
                continue;
            }

            // 重命名 / 复制：旧路径与新路径是紧随其后的两个 NUL 字段。
            if (index + 1 >= fields.Length)
            {
                return null;
            }

            records.Add(new NumstatRecord(additions, deletions, fields[index++], fields[index++]));
        }

        return records;
    }

    /// <summary>
    /// 解析 <c>git ls-tree -r -l &lt;commit&gt;</c> 的输出为「路径 → 字节大小」表；
    /// gitlink 等没有内容大小的条目（git 以 "-" 占位）值为 <see langword="null"/>。
    /// 无法解析时返回 <see langword="null"/>。
    /// </summary>
    public static Dictionary<string, long?>? ParseTreeSizes(string output)
    {
        var sizes = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // 行形如「<mode> <type> <sha>␣␣…␣<size>\t<path>」：制表符前是元数据、后面是路径。
            var separator = line.IndexOf('\t');
            if (separator < 0)
            {
                return null;
            }

            var metadata = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = line[(separator + 1)..];
            if (metadata.Length != 4)
            {
                return null;
            }

            sizes[path] = metadata[3] == "-" ? null : long.Parse(metadata[3], CultureInfo.InvariantCulture);
        }

        return sizes;
    }

    /// <summary>
    /// 合成变更文件清单：以 raw 记录为源（每个变更路径一条），关联 numstat 的行数与
    /// 两侧大小表的大小。展示大小取值——新增 / 修改 / 重命名取「更新后」版本，
    /// 删除取「更新前」版本；子模块指针没有内容大小，保持 <see langword="null"/>。
    /// </summary>
    public static List<ChangedFile> BuildChangedFiles(
        IReadOnlyList<RawChangeRecord> records,
        IReadOnlyList<NumstatRecord> numstats,
        IReadOnlyDictionary<string, long?> sizesAtBase,
        IReadOnlyDictionary<string, long?> sizesAtHead)
    {
        var numstatByPath = numstats.ToDictionary(record => record.CurrentPath, StringComparer.Ordinal);

        var files = new List<ChangedFile>(records.Count);
        foreach (var record in records)
        {
            var (kind, reason) = Classify(record);
            numstatByPath.TryGetValue(record.CurrentPath, out var numstat);

            // 展示大小：删除看「更新前」，其余看「更新后」（重命名 / 复制取新路径）。
            var sizeSide = kind == ChangeKind.Deleted ? sizesAtBase : sizesAtHead;
            sizeSide.TryGetValue(record.CurrentPath, out var sizeBytes);

            files.Add(new ChangedFile(
                record.CurrentPath,
                record.NewPath is null ? null : record.Path,
                kind,
                reason,
                numstat?.Additions,
                numstat?.Deletions,
                record.Score,
                sizeBytes,
                record.OldBlobHash,
                record.NewBlobHash));
        }

        return files;
    }

    /// <summary>
    /// 状态字母 → 变更类型的映射；gitlink 与无法识别的字母都收进「其他」。
    /// gitlink 必须排在 A/D/M 之前判断：新增 / 删除子模块时 git 报的是 A / D，但记录的是
    /// 指针而非文件内容（导出快照无内容可取），与「指针换了目标」同属一类；
    /// 否则会被误列为普通新增 / 删除，还会带上 numstat 编造的 +1 行数
    /// （实测 gitlink 无内容，行数只有 1/0、1/1、0/1 三种形，与文件行数无关）。
    /// </summary>
    private static (ChangeKind Kind, OtherChangeReason Reason) Classify(RawChangeRecord record)
        => record.StatusLetter switch
        {
            "A" or "D" or "M" when record.InvolvesSubmodule
                => (ChangeKind.Other, OtherChangeReason.SubmodulePointer),
            "A" => (ChangeKind.Added, OtherChangeReason.None),
            "D" => (ChangeKind.Deleted, OtherChangeReason.None),
            "M" => (ChangeKind.Modified, OtherChangeReason.None),
            "R" => (ChangeKind.Renamed, OtherChangeReason.None),
            "C" => (ChangeKind.Other, OtherChangeReason.Copied),
            "T" => (ChangeKind.Other, OtherChangeReason.TypeChange),
            "U" => (ChangeKind.Other, OtherChangeReason.Unmerged),
            _ => (ChangeKind.Other, OtherChangeReason.Unknown),
        };

    /// <summary>numstat 的行数数字（"-" 表示二进制 → null）。</summary>
    private static int? ParseLineCount(string field)
        => int.TryParse(field, CultureInfo.InvariantCulture, out var value) ? value : null;
}
