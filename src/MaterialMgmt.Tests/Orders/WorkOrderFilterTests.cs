using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.Tests.Orders;

public class WorkOrderFilterTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 16, 47, 0);

    [Fact]
    public void Empty_filter_matches_everything()
    {
        var f = new WorkOrderFilter();
        Assert.True(f.IsEmpty);
        Assert.True(f.Matches(OrderRules.Cancelled, Start, 6, "FML-000006"));
    }

    [Fact]
    public void Status_must_match()
    {
        var f = new WorkOrderFilter(Status: OrderRules.Completed);
        Assert.False(f.Matches(OrderRules.InProgress, Start, 1));
        Assert.True(f.Matches(OrderRules.Completed, Start, 1));
    }

    [Fact]
    public void Date_range_includes_both_end_days()
    {
        var f = new WorkOrderFilter(From: new DateTime(2026, 11, 10), To: new DateTime(2026, 11, 10));
        Assert.True(f.Matches(OrderRules.Completed, Start, 1));
        Assert.True(f.Matches(OrderRules.Completed, new DateTime(2026, 11, 10, 23, 59, 59), 1));
        Assert.False(f.Matches(OrderRules.Completed, new DateTime(2026, 11, 11), 1));
        Assert.False(f.Matches(OrderRules.Completed, new DateTime(2026, 11, 9, 23, 59, 59), 1));
    }

    [Fact]
    public void Combined_conditions_all_apply()
    {
        var f = new WorkOrderFilter(Status: OrderRules.Completed, FormulaId: 2, Text: "2026102");
        Assert.True(f.Matches(OrderRules.Completed, Start, 2, "FML-000002 2K模拟", "2026102101"));
        Assert.False(f.Matches(OrderRules.Completed, Start, 4, "FML-000004 4K模拟", "2026102201"));
        Assert.False(f.Matches(OrderRules.Completed, Start, 2, "FML-000002 2K模拟", "HT001"));
        Assert.False(f.Matches(OrderRules.Cancelled, Start, 2, "FML-000002 2K模拟", "2026102101"));
    }

    [Fact]
    public void Text_is_case_insensitive_and_null_safe()
    {
        var f = new WorkOrderFilter(Text: "fml");
        Assert.True(f.Matches(OrderRules.Completed, Start, 1, null, "FML-000001"));
        Assert.False(f.Matches(OrderRules.Completed, Start, 1, null, null));
    }
}
