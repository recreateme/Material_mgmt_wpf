namespace MaterialMgmt.Core.Orders;

/// <summary>首页“今日”统计的输入：一张工单的开始时间、状态、是否有过重称（存在作废记录）。</summary>
public sealed record OrderDayInfo(DateTime StartTime, string Status, bool HasRework);

/// <summary>首页“今日”统计：按开始时间落在当天计算。</summary>
public readonly record struct TodaySummary(int Started, int Completed, int WithRework)
{
    public static TodaySummary For(IEnumerable<OrderDayInfo> orders, DateTime today)
    {
        var day = today.Date;
        var todays = orders.Where(o => o.StartTime.Date == day).ToList();
        return new TodaySummary(
            todays.Count,
            todays.Count(o => o.Status == OrderRules.Completed),
            todays.Count(o => o.HasRework));
    }
}
