using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.Tests.Orders;

public class OrderRulesTests
{
    [Fact]
    public void EnsureCanDelete_BlocksInProgress()
    {
        var wo = new WorkOrder { Status = OrderRules.InProgress };
        var ex = Assert.Throws<InvalidOperationException>(() => OrderRules.EnsureCanDelete(wo, allowCompleted: true));
        Assert.Contains("进行中", ex.Message);
    }

    [Fact]
    public void EnsureCanDelete_AllowsCancelled()
    {
        var wo = new WorkOrder { Status = OrderRules.Cancelled };
        OrderRules.EnsureCanDelete(wo, allowCompleted: true);
    }

    [Fact]
    public void EnsureCanDelete_CompletedRequiresFlag()
    {
        var wo = new WorkOrder { Status = OrderRules.Completed };
        Assert.Throws<InvalidOperationException>(() => OrderRules.EnsureCanDelete(wo, allowCompleted: false));
        OrderRules.EnsureCanDelete(wo, allowCompleted: true);
    }
}
