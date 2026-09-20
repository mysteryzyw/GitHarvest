using System.Globalization;
using System.Text.RegularExpressions;

namespace GitHarvest.Core.Export;

/// <summary>
/// 更新日期目录名（更新包落在「输出路径/&lt;更新日期&gt;/」的那一层）的规则：
/// 默认名、合法性校验与重名让位。纯函数式工具（不碰文件系统，「目录是否已存在」由调用方
/// 以委托传入），因此可直接测试，也让编排层的时间与文件系统依赖保持可控。
/// </summary>
public static class ExportFolderName
{
    /// <summary>默认目录名的格式（spec 用户故事 31：默认 <c>yyyy-MM-dd</c>）。</summary>
    public const string DefaultFormat = "yyyy-MM-dd";

    /// <summary>重名时追加的时分秒格式（同日多次导出不互相覆盖）。</summary>
    public const string ConflictSuffixFormat = "HHmmss";

    /// <summary>同一秒内连续导出的兜底上限：超过则说明输出路径被人为占满，交由用户处理。</summary>
    private const int MaxCounterAttempts = 100;

    /// <summary>Windows 单个目录段的长度上限（NTFS 的 255 个 UTF-16 字符）。</summary>
    private const int MaxNameLength = 255;

    /// <summary>指定日期对应的默认目录名（<c>yyyy-MM-dd</c>，不受当前区域设置影响）。</summary>
    /// <param name="date">用于命名的时间点（由调用方提供，本类不读系统时钟）。</param>
    public static string DefaultFor(DateTimeOffset date)
        => date.ToString(DefaultFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// 校验用户编辑后的目录名能否安全地当作一个目录段使用。
    /// </summary>
    /// <param name="name">待校验的目录名（应当是单层目录名，不含任何分隔符）。</param>
    /// <returns>不合法时返回给用户看的中文原因；合法时返回 <see langword="null"/>。</returns>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "更新日期目录名不能为空。";
        }

        if (name.Length > MaxNameLength)
        {
            return $"更新日期目录名过长（{name.Length} 个字符），最多 {MaxNameLength} 个字符。";
        }

        // 目录名必须是单层：分隔符也按非法字符处理，否则会把更新包写到输出路径之外。
        if (WindowsNameRules.TryFindInvalidCharacter(name, treatPathSeparatorsAsInvalid: true, out var invalid))
        {
            return $"更新日期目录名不能包含{invalid}。";
        }

        // Windows 会静默去掉结尾的点与空格，导致实际目录名与用户输入不一致——直接拒绝。
        if (WindowsNameRules.EndsWithDotOrSpace(name))
        {
            return "更新日期目录名不能以点或空格结尾。";
        }

        if (WindowsNameRules.IsReservedName(name))
        {
            return $"「{name}」是 Windows 保留设备名，不能作为目录名。";
        }

        return null;
    }

    /// <summary>
    /// 解析实际可用的目录名：请求的名字没被占用就沿用，已占用则追加时分秒；
    /// 同一秒内再次冲突（连 _HHmmss 也被占用）时继续追加序号兜底。
    /// </summary>
    /// <param name="requestedName">用户请求的目录名（调用方应先用 <see cref="Validate"/> 校验）。</param>
    /// <param name="now">用于生成时分秒后缀的时间点。</param>
    /// <param name="nameExists">判断某个目录名是否已被占用（编排层传 <c>Directory.Exists</c>）。</param>
    public static string ResolveUnique(string requestedName, DateTimeOffset now, Func<string, bool> nameExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        ArgumentNullException.ThrowIfNull(nameExists);

        if (!nameExists(requestedName))
        {
            return requestedName;
        }

        var stamped = $"{requestedName}_{now.ToString(ConflictSuffixFormat, CultureInfo.InvariantCulture)}";
        if (!nameExists(stamped))
        {
            return stamped;
        }

        for (var counter = 2; counter <= MaxCounterAttempts; counter++)
        {
            var candidate = $"{stamped}_{counter}";
            if (!nameExists(candidate))
            {
                return candidate;
            }
        }

        // 走到这里说明同一秒内的候选名字全被占满，属于异常使用场景——抛出让调用方记日志。
        throw new InvalidOperationException($"目录名「{requestedName}」及其序号变体都已被占用。");
    }
}
