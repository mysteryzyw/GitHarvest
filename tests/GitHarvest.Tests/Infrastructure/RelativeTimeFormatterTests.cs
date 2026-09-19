using GitHarvest.Core.Infrastructure;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 相对时间格式化的分档规则：「刚刚 / N 分钟前 / N 小时前 / N 天前 / N 周前 / N 个月前 / N 年前」，
/// 用固定基准时间断言各档边界两侧。
/// </summary>
public sealed class RelativeTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    // 刚刚：不足 1 分钟；未来时间（时钟回拨）也归这档。
    [InlineData(0, "刚刚")]
    [InlineData(59, "刚刚")]
    [InlineData(-60, "刚刚")]
    // 分钟档：[1 分钟, 1 小时)。
    [InlineData(60, "1 分钟前")]
    [InlineData(60 * 59, "59 分钟前")]
    // 小时档：[1 小时, 1 天)。
    [InlineData(3600, "1 小时前")]
    [InlineData(3600 * 23, "23 小时前")]
    // 天档：[1 天, 7 天)。
    [InlineData(86400, "1 天前")]
    [InlineData(86400 * 6, "6 天前")]
    // 周档：[7 天, 28 天)。
    [InlineData(86400 * 7, "1 周前")]
    [InlineData(86400 * 13, "1 周前")]
    [InlineData(86400 * 27, "3 周前")]
    // 月档（30 天/月）：[28 天, 365 天)。
    [InlineData(86400 * 28, "1 个月前")]
    [InlineData(86400 * 60, "2 个月前")]
    [InlineData(86400 * 364, "12 个月前")]
    // 年档（365 天/年）：365 天起。
    [InlineData(86400 * 365, "1 年前")]
    [InlineData(86400 * 730, "2 年前")]
    public void 按距现在的时间跨度分档(int elapsedSeconds, string expected)
    {
        var time = Now.AddSeconds(-elapsedSeconds);

        Assert.Equal(expected, RelativeTimeFormatter.Format(time, Now));
    }
}
