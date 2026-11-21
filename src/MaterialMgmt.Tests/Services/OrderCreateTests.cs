using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Services;

public class OrderCreateTests : TestHost
{
    private OrderService Svc => Get<OrderService>();

    [Fact]
    public void Create_StoresParams_InProgress()
    {
        var (f, _) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var op = Get<UserService>().CreateBusiness("zhangsan", "张三", "Qwe-7788");
        var id = Svc.Create(f.Id, op.Id, "张三", new WorkOrderParams
        {
            Temperature = 25.5, Humidity = 40, ProductionNo = "P-001", BaseFilm = "BF", BatchNo = "B1",
            MixingException = true, MixingExceptionNote = "结块"
        });

        var wo = Svc.GetDetail(id);
        Assert.Equal(OrderRules.InProgress, wo.Status);
        Assert.Equal((op.Id, "张三", 25.5, "P-001"), (wo.OperatorId!.Value, wo.Operator, wo.Temperature!.Value, wo.ProductionNo));
        Assert.Equal("结块", wo.MixingExceptionNote);
        Assert.Contains(Svc.ActiveOrders(op.Id), o => o.Id == id);
    }

    [Fact]
    public void Create_FormulaWithoutSteps_Throws()
    {
        var (f, _) = AddFormula("FML-T00001");
        Assert.False(Svc.FormulaHasSteps(f.Id));
        Assert.Throws<InvalidOperationException>(() => Svc.Create(f.Id, 1, "x", new WorkOrderParams()));
        Assert.Throws<InvalidOperationException>(() => Svc.Create(9999, 1, "x", new WorkOrderParams()));
    }

    [Fact]
    public void UpdateExceptionNotes_TrimsAndClearsUnchecked()
    {
        var (f, _) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var wo = AddOrder(f.Id);

        Svc.UpdateExceptionNotes(wo.Id, true, "  有气泡 ", false, "不应保存");
        var d = Svc.GetDetail(wo.Id);
        Assert.Equal((true, "有气泡", false, (string?)null),
            (d.MixingException!.Value, d.MixingExceptionNote, d.CoatingException!.Value, d.CoatingExceptionNote));

        Svc.UpdateExceptionNotes(wo.Id, false, "x", true, "划痕");
        d = Svc.GetDetail(wo.Id);
        Assert.Null(d.MixingExceptionNote);
        Assert.Equal("划痕", d.CoatingExceptionNote);
    }

    [Fact]
    public void GetDetail_IncludesVoidedRecords()
    {
        var (f, items) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var wo = AddOrder(f.Id);
        Svc.SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.2, DateTime.Now);
        Svc.InvalidateValidRecords(wo.Id, items[0].Id, "tester", "重称");
        Svc.SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.5, DateTime.Now);

        var d = Svc.GetDetail(wo.Id);
        Assert.Equal(2, d.Records.Count);
        Assert.Single(d.Records, r => !r.IsValid && r.VoidReason == "重称");
        Assert.All(d.Records, r => Assert.Equal("甲", r.FormulaItem.Material.Name));
    }
}
