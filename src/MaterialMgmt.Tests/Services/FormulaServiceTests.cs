using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Services;

public class FormulaServiceTests : TestHost
{
    private FormulaService Svc => Get<FormulaService>();
    private Material _a = null!, _b = null!, _c = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _a = AddMaterial("MAT-T00001", "甲");
        _b = AddMaterial("MAT-T00002", "乙");
        _c = AddMaterial("MAT-T00003", "丙");
    }

    private List<(int Seq, int MaterialId)> Steps(int formulaId)
        => Svc.GetWithSteps(formulaId)!.Items.Select(i => (i.Sequence, i.MaterialId)).ToList();

    [Fact]
    public void AddStep_WithoutAnchor_Appends()
    {
        var (f, _) = AddFormula("FML-T00001", (_a, 1, 2));
        Svc.AddStep(f.Id, new StepInput(_b.Id, 3, 4, null), null);
        Assert.Equal(new[] { (1, _a.Id), (2, _b.Id) }, Steps(f.Id));
    }

    [Fact]
    public void AddStep_AfterAnchor_InsertsAndRenumbers()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2));
        var id = Svc.AddStep(f.Id, new StepInput(_c.Id, 5, 6, "备注"), items[0].Id);

        Assert.Equal(new[] { (1, _a.Id), (2, _c.Id), (3, _b.Id) }, Steps(f.Id));
        var added = Svc.GetWithSteps(f.Id)!.Items.Single(i => i.Id == id);
        Assert.Equal((5.0, 6.0, "备注"), (added.WeightMin, added.WeightMax, added.Notes));
    }

    [Fact]
    public void UpdateStep_ChangesValues()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2));
        Svc.UpdateStep(items[0].Id, new StepInput(_b.Id, 7, 8, "x"));
        var item = Svc.GetWithSteps(f.Id)!.Items.Single();
        Assert.Equal((_b.Id, 7.0, 8.0, "x"), (item.MaterialId, item.WeightMin, item.WeightMax, item.Notes));
    }

    [Fact]
    public void Reorder_AppliesGivenOrder()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2), (_c, 1, 2));
        Svc.Reorder(f.Id, new[] { items[2].Id, items[0].Id, items[1].Id });
        Assert.Equal(new[] { (1, _c.Id), (2, _a.Id), (3, _b.Id) }, Steps(f.Id));
    }

    [Fact]
    public void Reorder_WithStaleList_Throws()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2));
        Assert.Throws<InvalidOperationException>(() => Svc.Reorder(f.Id, new[] { items[0].Id }));
    }

    [Fact]
    public void DeleteSteps_RenumbersRemaining()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2), (_c, 1, 2));
        Svc.DeleteSteps(f.Id, new[] { items[1].Id });
        Assert.Equal(new[] { (1, _a.Id), (2, _c.Id) }, Steps(f.Id));
    }

    [Fact]
    public void DeleteSteps_StepWithRecords_DeletesNothing()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2));
        var wo = AddOrder(f.Id, OrderRules.Completed);
        Get<OrderService>().SavePendingWeight(wo.Id, items[0].Id, "MAT-T00001", 1.5, DateTime.Now);

        Assert.Throws<InvalidOperationException>(() => Svc.DeleteSteps(f.Id, new[] { items[0].Id, items[1].Id }));
        Assert.Equal(2, Steps(f.Id).Count);
    }

    [Fact]
    public void StepEdits_AreLocked_WhileOrderInProgress()
    {
        var (f, items) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 1, 2));
        AddOrder(f.Id, OrderRules.InProgress);

        Assert.Throws<InvalidOperationException>(() => Svc.AddStep(f.Id, new StepInput(_c.Id, 1, 2, null), null));
        Assert.Throws<InvalidOperationException>(() => Svc.UpdateStep(items[0].Id, new StepInput(_c.Id, 1, 2, null)));
        Assert.Throws<InvalidOperationException>(() => Svc.Reorder(f.Id, new[] { items[1].Id, items[0].Id }));
        Assert.Throws<InvalidOperationException>(() => Svc.DeleteSteps(f.Id, new[] { items[0].Id }));
        Assert.Equal(new[] { (1, _a.Id), (2, _b.Id) }, Steps(f.Id));
    }

    [Fact]
    public void Clone_CopiesStepsInOrder()
    {
        var (f, _) = AddFormula("FML-T00001", (_a, 1, 2), (_b, 3, 4));
        var copy = Svc.Clone(f.Id, null, "tester");
        Assert.NotEqual(f.Code, copy.Code);
        Assert.Equal(Steps(f.Id), Steps(copy.Id));
    }

    [Fact]
    public void DeleteMany_FormulaWithOrders_DeletesNothing()
    {
        var (used, _) = AddFormula("FML-T00001", (_a, 1, 2));
        var (free, _) = AddFormula("FML-T00002", (_b, 1, 2));
        AddOrder(used.Id, OrderRules.Completed);

        Assert.Equal(new[] { used.Id }, Svc.WithWorkOrders(new[] { used.Id, free.Id }).Select(x => x.Id));
        Assert.Throws<InvalidOperationException>(() => Svc.DeleteMany(new[] { used.Id, free.Id }));
        Assert.Equal(2, Svc.All().Count);

        Svc.DeleteMany(new[] { free.Id });
        Assert.Equal(new[] { used.Id }, Svc.All().Select(x => x.Id));
    }

    [Fact]
    public void ToggleActive_FlipsAndFiltersActiveOnly()
    {
        var (f, _) = AddFormula("FML-T00001", (_a, 1, 2));
        Svc.ToggleActive(new[] { f.Id });
        Assert.Empty(Svc.All(activeOnly: true));
        Assert.Single(Svc.All());
    }
}
