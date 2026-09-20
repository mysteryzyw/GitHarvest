using System.Globalization;

namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 文件大小的展示格式化（「512 B」「8.2 KB」「18.0 MB」），对齐原型清单条目的
/// <c>.f-size</c> 风格：1024 进制，KB 起保留一位小数。纯函数。
/// </summary>
public static class FileSizeFormatter
{
    /// <summary>KB 及以上各级的进制（与文件管理器习惯一致）。</summary>
    private const long UnitStep = 1024;

    /// <summary>
    /// 把字节数格式化为展示文本：不足 1 KB 显示「N B」，其后 KB / MB / GB（一位小数）；
    /// <see langword="null"/>（子模块指针等没有内容大小的条目）显示「—」。
    /// </summary>
    public static string Format(long? sizeBytes) => sizeBytes switch
    {
        null => "—",
        < UnitStep => $"{sizeBytes} B",
        < UnitStep * UnitStep => FormatOneDecimal(sizeBytes.Value, 1, "KB"),
        < UnitStep * UnitStep * UnitStep => FormatOneDecimal(sizeBytes.Value, 2, "MB"),
        _ => FormatOneDecimal(sizeBytes.Value, 3, "GB"),
    };

    /// <summary>按一位小数格式化（<paramref name="unitPower"/> 次进制换算到目标单位），固定不变文化避免环境差异。</summary>
    private static string FormatOneDecimal(long sizeBytes, int unitPower, string unit)
    {
        var valueInUnits = sizeBytes / Math.Pow(UnitStep, unitPower);
        return string.Create(CultureInfo.InvariantCulture, $"{valueInUnits:0.0} {unit}");
    }
}
