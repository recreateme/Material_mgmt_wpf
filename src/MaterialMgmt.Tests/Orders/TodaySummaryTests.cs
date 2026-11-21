using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.Tests.Orders;

public class TodaySummaryTests
{
    private static readonly DateTime Today = new(2026, 11, 22, 15, 0, 0);

    [Fact]
    public void CountsOnlyOrdersStartedToday()
    {
        var orders = new[]
        {
            new OrderDayInfo(new DateTime(2026, 11, 22, 0, 0, 0), OrderRules.Completed, false),
            new OrderDayInfo(new DateTime(2026, 11, 22, 23, 59, 59), OrderRules.InProgress, true),
            new OrderDayInfo(new DateTime(2026, 11, 22, 9, 0, 0), OrderRules.Cancelled, true),
            new OrderDayInfo(new DateTime(2026, 11, 21, 23, 59, 59), OrderRules.Completed, true),
            new OrderDayInfo(new DateTime(2026, 11, 23, 0, 0, 0), OrderRules.Completed, false)
        };

        var s = TodaySummary.For(orders, Today);

        Assert.Equal(3, s.Started);
        Assert.Equal(1, s.Completed);
        Assert.Equal(2, s.WithRework);
    }

    [Fact]
    public void Empty()
    {
        Assert.Equal(new TodaySummary(0, 0, 0), TodaySummary.For(Array.Empty<OrderDayInfo>(), Today));
    }
}
