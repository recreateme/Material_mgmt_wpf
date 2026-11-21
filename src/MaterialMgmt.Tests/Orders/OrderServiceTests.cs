using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Orders;

public class OrderServiceTests : IAsyncLifetime
{
    private string _root = "";
    private ServiceProvider _sp = null!;
    private OrderService _orders = null!;
    private IDbContextFactory<MaterialMgmtDbContext> _factory = null!;
    private int _orderId;
    private int _itemId;

    public async Task InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "mm_orders_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(_root);
        _sp = services.BuildServiceProvider();
        await _sp.GetRequiredService<DbBootstrap>().InitializeAsync();
        _orders = _sp.GetRequiredService<OrderService>();
        _factory = _sp.GetRequiredService<IDbContextFactory<MaterialMgmtDbContext>>();

        await using var db = await _factory.CreateDbContextAsync();
        var mat = new Material { Code = "MAT-T00001", Name = "测试料" };
        var fml = new Formula { Code = "FML-T00001", Name = "测试配方" };
        db.Materials.Add(mat);
        db.Formulas.Add(fml);
        await db.SaveChangesAsync();
        var item = new FormulaItem { FormulaId = fml.Id, MaterialId = mat.Id, Sequence = 1, WeightMin = 1, WeightMax = 2 };
        db.FormulaItems.Add(item);
        var wo = new WorkOrder { FormulaId = fml.Id, Operator = "tester", Status = "in_progress" };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        _itemId = item.Id;
        _orderId = wo.Id;
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        SqliteConnectionPoolClear();
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录，忽略 */ }
    }

    private static void SqliteConnectionPoolClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    [Fact]
    public async Task SavePendingWeight_StoresSnapshot_ThatSurvivesFormulaEdits()
    {
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var item = await db.FormulaItems.FirstAsync(i => i.Id == _itemId);
            item.WeightMin = 10; item.WeightMax = 20; // 事后改配方
            await db.SaveChangesAsync();
        }

        await using var db2 = await _factory.CreateDbContextAsync();
        var rec = await db2.WeightRecords.SingleAsync();
        Assert.Equal(1, rec.WeightMinSnapshot);
        Assert.Equal(2, rec.WeightMaxSnapshot);
        Assert.Equal("测试料", rec.MaterialNameSnapshot);
    }

    [Fact]
    public void SecondValidRecord_ForSameStep_IsRejectedByDatabase()
    {
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now);
        Assert.ThrowsAny<Exception>(() =>
            _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.6, DateTime.Now));
    }

    [Fact]
    public async Task Invalidate_RecordsVoidTrace_AndAllowsReweigh()
    {
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now);
        var n = _orders.InvalidateValidRecords(_orderId, _itemId, "tester");
        Assert.Equal(1, n);
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.4, DateTime.Now); // 作废后可重称

        await using var db = await _factory.CreateDbContextAsync();
        var voided = await db.WeightRecords.SingleAsync(r => !r.IsValid);
        Assert.NotNull(voided.VoidedAt);
        Assert.Equal("tester", voided.VoidedBy);
        Assert.Equal(1, await db.WeightRecords.CountAsync(r => r.IsValid));
    }

    [Fact]
    public async Task Delete_IsSoft_KeepsOrderAndRecords()
    {
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now);
        _orders.Cancel(_orderId);
        _orders.DeleteMany(new[] { _orderId }, allowCompleted: true, deletedBy: "tester");

        await using var db = await _factory.CreateDbContextAsync();
        var wo = await db.WorkOrders.SingleAsync(w => w.Id == _orderId);
        Assert.NotNull(wo.DeletedAt);
        Assert.Equal("tester", wo.DeletedBy);
        Assert.Equal(1, await db.WeightRecords.CountAsync(r => r.WorkOrderId == _orderId));
    }

    [Fact]
    public async Task Invalidate_StoresReason_AndSaveStoresScanSource()
    {
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now, ScanSources.Serial);
        _orders.InvalidateValidRecords(_orderId, _itemId, "tester", VoidReasons.Compose(VoidReasons.Other, " 秤盘有残料 "));

        await using var db = await _factory.CreateDbContextAsync();
        var voided = await db.WeightRecords.SingleAsync(r => !r.IsValid);
        Assert.Equal("其他：秤盘有残料", voided.VoidReason);
        Assert.Equal(ScanSources.Serial, voided.ScanSource);
    }

    [Fact]
    public async Task FormulaSteps_LockedWhileOrderInProgress_UnlockedAfterComplete()
    {
        int formulaId;
        await using (var db = await _factory.CreateDbContextAsync())
            formulaId = (await db.WorkOrders.SingleAsync(w => w.Id == _orderId)).FormulaId;

        var active = _orders.ActiveOrdersForFormula(formulaId);
        Assert.Single(active);
        var ex = Assert.Throws<InvalidOperationException>(() => _orders.EnsureFormulaStepsEditable(formulaId));
        Assert.Contains($"#{_orderId}", ex.Message);

        _orders.CompleteWorkOrder(_orderId);
        Assert.Empty(_orders.ActiveOrdersForFormula(formulaId));
        _orders.EnsureFormulaStepsEditable(formulaId);
    }

    [Fact]
    public void StepsWithRecords_IncludesVoidedRecords()
    {
        Assert.Empty(_orders.StepsWithRecords(new[] { _itemId }));
        _orders.SavePendingWeight(_orderId, _itemId, "MAT-T00001", 1.5, DateTime.Now);
        _orders.InvalidateValidRecords(_orderId, _itemId, "tester", VoidReasons.ReadingAbnormal);
        Assert.Contains(_itemId, _orders.StepsWithRecords(new[] { _itemId }));
    }

    [Fact]
    public async Task RepairDuplicateValidRecords_KeepsLatest_AndIndexCanBeCreated()
    {
        await using (var db = await _factory.CreateDbContextAsync())
        {
            // 模拟没有唯一索引的旧库里留下的重复有效记录
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ux_weight_records_valid");
            db.WeightRecords.Add(new WeightRecord { WorkOrderId = _orderId, FormulaItemId = _itemId, ActualWeight = 1.1, Timestamp = DateTime.Now.AddMinutes(-2) });
            db.WeightRecords.Add(new WeightRecord { WorkOrderId = _orderId, FormulaItemId = _itemId, ActualWeight = 1.2, Timestamp = DateTime.Now });
            await db.SaveChangesAsync();
        }
        var boot = _sp.GetRequiredService<DbBootstrap>();
        Assert.Equal(1, _orders.CountDuplicateValidGroups());

        Assert.Equal(1, _orders.RepairDuplicateValidRecords("admin"));
        Assert.Equal(0, _orders.CountDuplicateValidGroups());
        Assert.True(await boot.TryCreateValidIndexAsync());

        await using var db2 = await _factory.CreateDbContextAsync();
        var valid = await db2.WeightRecords.SingleAsync(r => r.IsValid);
        Assert.Equal(1.2, valid.ActualWeight);
        var voided = await db2.WeightRecords.SingleAsync(r => !r.IsValid);
        Assert.Equal(VoidReasons.SystemRepair, voided.VoidReason);
    }

    [Fact]
    public async Task Backup_KeepsFilesPerReason()
    {
        var boot = _sp.GetRequiredService<DbBootstrap>();
        var path = await boot.BackupAsync("manual");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.NotNull(boot.LatestBackupTime());
    }

    [Fact]
    public async Task DeleteMany_IsAtomic_WhenOneOrderIsInProgress()
    {
        int doneId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var done = new WorkOrder { FormulaId = (await db.Formulas.FirstAsync()).Id, Operator = "t", Status = "completed" };
            db.WorkOrders.Add(done);
            await db.SaveChangesAsync();
            doneId = done.Id;
        }

        Assert.Throws<InvalidOperationException>(() =>
            _orders.DeleteMany(new[] { doneId, _orderId }, allowCompleted: true)); // _orderId 仍进行中

        await using var db2 = await _factory.CreateDbContextAsync();
        Assert.Equal(0, await db2.WorkOrders.CountAsync(w => w.DeletedAt != null));
    }
}
