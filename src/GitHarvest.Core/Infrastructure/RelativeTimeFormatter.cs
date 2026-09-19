namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 中文相对时间格式化（「3 天前」「2 周前」），对齐原型提交/分支摘要里的时间风格。
/// 纯函数：「现在」由调用方传入，便于测试也便于同一次渲染里所有条目用同一基准。
/// </summary>
public static class RelativeTimeFormatter
{
    /// <summary>
    /// 把 <paramref name="time"/> 格式化为相对 <paramref name="now"/> 的中文相对时间：
    /// 1 分钟内「刚刚」，其后依次 分钟/小时/天（7 天内）/周（4 周内）/月（365 天内）/年。
    /// 未来时间（时钟回拨等）按「刚刚」处理。
    /// </summary>
    public static string Format(DateTimeOffset time, DateTimeOffset now)
    {
        var elapsed = now - time;

        // 未来时间（时钟回拨、提交时间写在未来）按「刚刚」处理，不出现负数文案。
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours} 小时前";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays} 天前";
        }

        if (elapsed < TimeSpan.FromDays(28))
        {
            return $"{(int)(elapsed.TotalDays / 7)} 周前";
        }

        // 月/年按 30/365 天的固定步长取整：这里要的是列表预览里的大致体感，不是日历精度。
        // 月档起点 28 天不足 30 天的一步，取整会得 0——至少按 1 个月计。
        if (elapsed < TimeSpan.FromDays(365))
        {
            return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))} 个月前";
        }

        return $"{(int)(elapsed.TotalDays / 365)} 年前";
    }
}
