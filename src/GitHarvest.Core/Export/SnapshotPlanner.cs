using GitHarvest.Core.Git;

namespace GitHarvest.Core.Export;

/// <summary>快照落在更新包里的哪一侧文件夹（CONTEXT.md「更新前」/「更新后」）。</summary>
public enum SnapshotSide
{
    /// <summary>「更新前」文件夹：存放变更范围内文件的旧版本快照（删除的文件只出现在这里）。</summary>
    Before,

    /// <summary>「更新后」文件夹：存放变更范围内文件的新版本快照（新增的文件只出现在这里）。</summary>
    After,
}

/// <summary>
/// 一条待写出的快照：目标侧、相对仓库根的文件路径、内容所在的 blob 哈希。
/// </summary>
/// <param name="Side">写进「更新前」还是「更新后」。</param>
/// <param name="RelativePath">相对仓库根的文件路径（正斜杠风格，与 git 输出一致；写出时再转成系统分隔符）。</param>
/// <param name="BlobHash">内容所在的 blob 完整哈希（由 IGitService 按它取内容）。</param>
public sealed record SnapshotEntry(SnapshotSide Side, string RelativePath, string BlobHash);

/// <summary>
/// 快照规划结果：两侧待写出的条目全集（顺序与变更清单一致，便于进度按文件推进）。
/// </summary>
/// <param name="Entries">全部待写条目（更新前 + 更新后）。</param>
public sealed record SnapshotPlan(IReadOnlyList<SnapshotEntry> Entries)
{
    /// <summary>「更新前」文件夹将包含的文件数（原型「更新前（N 个文件）」）。</summary>
    public int BeforeCount => Entries.Count(entry => entry.Side == SnapshotSide.Before);

    /// <summary>「更新后」文件夹将包含的文件数（原型「更新后（N 个文件）」）。</summary>
    public int AfterCount => Entries.Count(entry => entry.Side == SnapshotSide.After);

    /// <summary>计划里没有任何待写文件（理论上只在空变更范围时出现——空范围由编排层先行拒绝）。</summary>
    public bool IsEmpty => Entries.Count == 0;
}

/// <summary>
/// 把变更清单规划成快照落位（纯函数，不碰 git 与文件系统）。
/// 落位规则只有一条主线：**哪一侧有内容（blob 哈希非空）就写哪一侧**——
/// git 的双点差异天然给出「新增只有新侧、删除只有旧侧、修改与类型变更两侧都有」；
/// 路径上再做一处修正：重命名 / 复制的「更新前」用旧路径、「更新后」用新路径（用户故事 24）。
/// 例外只有子模块：gitlink 的哈希指向另一个仓库的提交，不是本仓库的 blob，
/// 按它取内容必然失败，因此一律不写快照（它在清单与更新说明里照旧可见，用户故事 28）。
/// </summary>
public static class SnapshotPlanner
{
    /// <summary>按变更清单生成快照计划。</summary>
    /// <param name="files">变更范围内的全部文件（顺序即写出顺序）。</param>
    public static SnapshotPlan Plan(IReadOnlyList<ChangedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var entries = new List<SnapshotEntry>(files.Count);
        foreach (var file in files)
        {
            if (IsContentLess(file))
            {
                continue;
            }

            // 更新前用「旧路径」：重命名 / 复制的旧版本在仓库里是旧路径，新版本在新路径。
            if (file.OldBlobHash is { } beforeBlob)
            {
                entries.Add(new SnapshotEntry(SnapshotSide.Before, file.OldPath ?? file.Path, beforeBlob));
            }

            if (file.NewBlobHash is { } afterBlob)
            {
                entries.Add(new SnapshotEntry(SnapshotSide.After, file.Path, afterBlob));
            }
        }

        return new SnapshotPlan(entries);
    }

    /// <summary>
    /// 该条目在仓库里没有可取的文件内容（目前只有子模块指针）。
    /// 判据只看「原因」而不看「类型」：现实中的子模块条目一律被归成「其他」，
    /// 以原因判定可让「将来归类若漏掉子模块」也不至于写出注定失败的内容。
    /// </summary>
    private static bool IsContentLess(ChangedFile file)
        => file.OtherReason == OtherChangeReason.SubmodulePointer;
}
