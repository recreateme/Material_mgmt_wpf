using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class OrderService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;

    public OrderService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    public void Cancel(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        var wo = db.WorkOrders.FirstOrDefault(x => x.Id == workOrderId)
                 ?? throw new InvalidOperationException($"工单 {workOrderId} 不存在");
        OrderRules.Cancel(wo);
        db.SaveChanges();
    }

    /// <summary>软删除：工单从列表消失，但工单与称重记录保留以备追溯。</summary>
    public void Delete(int workOrderId, bool allowCompleted = false, string? deletedBy = null)
        => DeleteMany(new[] { workOrderId }, allowCompleted, deletedBy);

    /// <summary>批量软删除，单事务：要么全部成功，要么全部不变。</summary>
    public int DeleteMany(IEnumerable<int> ids, bool allowCompleted = false, string? deletedBy = null)
    {
        var list = ids.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        var orders = db.WorkOrders.Where(x => list.Contains(x.Id) && x.DeletedAt == null).ToList();
        var missing = list.Except(orders.Select(o => o.Id)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"工单 {string.Join(",", missing)} 不存在或已删除");

        foreach (var wo in orders)
            OrderRules.EnsureCanDelete(wo, allowCompleted);

        var now = DateTime.Now;
        foreach (var wo in orders)
        {
            wo.DeletedAt = now;
            wo.DeletedBy = deletedBy;
        }
        db.SaveChanges();
        return orders.Count;
    }

    public WeightRecord SavePendingWeight(
        int workOrderId, int formulaItemId, string barcode, double weight, DateTime timestamp)
    {
        using var db = _factory.CreateDbContext();
        // 落库时固化当时的限值与原料名称，之后改配方不影响历史判定
        var item = db.FormulaItems.Include(i => i.Material).FirstOrDefault(i => i.Id == formulaItemId);
        var record = new WeightRecord
        {
            WorkOrderId = workOrderId,
            FormulaItemId = formulaItemId,
            BarcodeScanned = barcode,
            ActualWeight = weight,
            Timestamp = timestamp,
            IsValid = true,
            WeightMinSnapshot = item?.WeightMin,
            WeightMaxSnapshot = item?.WeightMax,
            MaterialCodeSnapshot = item?.Material?.Code,
            MaterialNameSnapshot = item?.Material?.Name
        };
        db.WeightRecords.Add(record);
        db.SaveChanges();
        return record;
    }

    public bool HasValidRecord(int workOrderId, int formulaItemId)
    {
        using var db = _factory.CreateDbContext();
        return db.WeightRecords.Any(r =>
            r.WorkOrderId == workOrderId &&
            r.FormulaItemId == formulaItemId &&
            r.IsValid);
    }

    /// <summary>作废某步全部有效称重记录（回称前调用）。返回作废条数。</summary>
    public int InvalidateValidRecords(int workOrderId, int formulaItemId, string? voidedBy = null)
    {
        using var db = _factory.CreateDbContext();
        var list = db.WeightRecords
            .Where(r => r.WorkOrderId == workOrderId && r.FormulaItemId == formulaItemId && r.IsValid)
            .ToList();
        var now = DateTime.Now;
        foreach (var r in list)
        {
            r.IsValid = false;
            r.VoidedAt = now;
            r.VoidedBy = voidedBy;
        }
        db.SaveChanges();
        return list.Count;
    }

    public HashSet<int> GetValidFormulaItemIds(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        return db.WeightRecords
            .Where(r => r.WorkOrderId == workOrderId && r.IsValid)
            .Select(r => r.FormulaItemId)
            .ToHashSet();
    }

    public void CompleteWorkOrder(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        var wo = db.WorkOrders.FirstOrDefault(x => x.Id == workOrderId)
                 ?? throw new InvalidOperationException($"工单 {workOrderId} 不存在");
        wo.Status = OrderRules.Completed;
        wo.EndTime = DateTime.Now;
        db.SaveChanges();
    }
}
