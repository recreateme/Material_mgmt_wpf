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

    public void Delete(int workOrderId, bool allowCompleted = false)
    {
        using var db = _factory.CreateDbContext();
        var wo = db.WorkOrders.FirstOrDefault(x => x.Id == workOrderId)
                 ?? throw new InvalidOperationException($"工单 {workOrderId} 不存在");
        OrderRules.EnsureCanDelete(wo, allowCompleted);
        db.WeightRecords.RemoveRange(db.WeightRecords.Where(r => r.WorkOrderId == workOrderId));
        db.WorkOrders.Remove(wo);
        db.SaveChanges();
    }

    public int DeleteMany(IEnumerable<int> ids, bool allowCompleted = false)
    {
        var list = ids.ToList();
        var n = 0;
        foreach (var id in list)
        {
            Delete(id, allowCompleted);
            n++;
        }
        return n;
    }

    public WeightRecord SavePendingWeight(
        int workOrderId, int formulaItemId, string barcode, double weight, DateTime timestamp)
    {
        using var db = _factory.CreateDbContext();
        var record = new WeightRecord
        {
            WorkOrderId = workOrderId,
            FormulaItemId = formulaItemId,
            BarcodeScanned = barcode,
            ActualWeight = weight,
            Timestamp = timestamp,
            IsValid = true
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
