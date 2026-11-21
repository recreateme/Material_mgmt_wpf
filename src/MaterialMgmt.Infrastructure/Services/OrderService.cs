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
        int workOrderId, int formulaItemId, string barcode, double weight, DateTime timestamp,
        string? scanSource = null)
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
            MaterialNameSnapshot = item?.Material?.Name,
            ScanSource = scanSource
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
    public int InvalidateValidRecords(int workOrderId, int formulaItemId, string? voidedBy = null,
        string? reason = null)
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
            r.VoidReason = reason;
        }
        db.SaveChanges();
        return list.Count;
    }

    /// <summary>引用该配方且仍在进行中的工单（未删除），按 ID 升序。</summary>
    public List<ActiveOrderRef> ActiveOrdersForFormula(int formulaId)
    {
        using var db = _factory.CreateDbContext();
        return db.WorkOrders
            .Where(w => w.FormulaId == formulaId && w.Status == OrderRules.InProgress && w.DeletedAt == null)
            .OrderBy(w => w.Id)
            .Select(w => new ActiveOrderRef(w.Id, w.ProductionNo, w.Operator))
            .ToList();
    }

    /// <summary>
    /// 配方步骤可修改的前置校验：被进行中工单引用时抛 <see cref="InvalidOperationException"/>。
    /// 保存前调用，防止编辑页打开期间有人开了新工单。
    /// </summary>
    public void EnsureFormulaStepsEditable(int formulaId)
    {
        var active = ActiveOrdersForFormula(formulaId);
        if (active.Count > 0)
            throw new InvalidOperationException(DescribeActiveOrders(active) + "，完成或取消后才能修改步骤。");
    }

    public static string DescribeActiveOrders(IReadOnlyList<ActiveOrderRef> active)
    {
        var list = string.Join("、", active.Take(5).Select(a =>
            string.IsNullOrWhiteSpace(a.ProductionNo) ? $"#{a.Id}" : $"#{a.Id}（{a.ProductionNo}）"));
        if (active.Count > 5) list += " 等";
        return $"该配方有 {active.Count} 张进行中工单：{list}";
    }

    /// <summary>给定步骤中已有称重记录（含作废记录）的步骤 ID。这些步骤删除后历史记录将无法追溯，必须保留。</summary>
    public HashSet<int> StepsWithRecords(IEnumerable<int> formulaItemIds)
    {
        var ids = formulaItemIds.ToList();
        using var db = _factory.CreateDbContext();
        return db.WeightRecords
            .Where(r => ids.Contains(r.FormulaItemId))
            .Select(r => r.FormulaItemId)
            .Distinct()
            .ToHashSet();
    }

    /// <summary>同一工单同一步骤存在多条有效记录的组数（正常应为 0）。</summary>
    public int CountDuplicateValidGroups()
    {
        using var db = _factory.CreateDbContext();
        return db.WeightRecords
            .Where(r => r.IsValid)
            .GroupBy(r => new { r.WorkOrderId, r.FormulaItemId })
            .Count(g => g.Count() > 1);
    }

    /// <summary>
    /// 修复重复有效记录：每组保留最新一条，其余标为作废（原因“系统修复”），单事务。返回作废条数。
    /// </summary>
    public int RepairDuplicateValidRecords(string? voidedBy)
    {
        using var db = _factory.CreateDbContext();
        var dupes = db.WeightRecords
            .Where(r => r.IsValid)
            .AsEnumerable()
            .GroupBy(r => (r.WorkOrderId, r.FormulaItemId))
            .Where(g => g.Count() > 1)
            .ToList();
        var now = DateTime.Now;
        var n = 0;
        foreach (var g in dupes)
        {
            foreach (var r in g.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id).Skip(1))
            {
                r.IsValid = false;
                r.VoidedAt = now;
                r.VoidedBy = voidedBy;
                r.VoidReason = VoidReasons.SystemRepair;
                n++;
            }
        }
        db.SaveChanges();
        return n;
    }

    public HashSet<int> GetValidFormulaItemIds(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        return db.WeightRecords
            .Where(r => r.WorkOrderId == workOrderId && r.IsValid)
            .Select(r => r.FormulaItemId)
            .ToHashSet();
    }

    /// <summary>各步骤当前有效记录的实测重量（同一步多条有效记录时取最新）。</summary>
    public Dictionary<int, double> GetValidWeights(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        return db.WeightRecords
            .Where(r => r.WorkOrderId == workOrderId && r.IsValid)
            .OrderBy(r => r.Timestamp)
            .Select(r => new { r.FormulaItemId, r.ActualWeight })
            .AsEnumerable()
            .GroupBy(x => x.FormulaItemId)
            .ToDictionary(g => g.Key, g => g.Last().ActualWeight);
    }

    /// <summary>完工汇总：起止时间、有效步骤数、重称（作废）次数。</summary>
    public OrderSummary GetSummary(int workOrderId)
    {
        using var db = _factory.CreateDbContext();
        var wo = db.WorkOrders.First(x => x.Id == workOrderId);
        var valid = db.WeightRecords.Count(r => r.WorkOrderId == workOrderId && r.IsValid);
        var voided = db.WeightRecords.Count(r => r.WorkOrderId == workOrderId && !r.IsValid);
        return new OrderSummary(wo.StartTime, wo.EndTime, valid, voided);
    }

    /// <summary>全部进行中工单（未删除）及其进度，可按操作员过滤；按开始时间倒序。</summary>
    public List<ActiveOrderInfo> ActiveOrders(int? operatorId = null)
    {
        using var db = _factory.CreateDbContext();
        var query = db.WorkOrders.Where(w => w.Status == OrderRules.InProgress && w.DeletedAt == null);
        if (operatorId is int op) query = query.Where(w => w.OperatorId == op);
        return query
            .OrderByDescending(w => w.StartTime)
            .Select(w => new ActiveOrderInfo(
                w.Id, w.FormulaId, w.Formula.Code, w.Formula.Name, w.OperatorId, w.Operator,
                w.ProductionNo, w.StartTime,
                db.WeightRecords.Where(r => r.WorkOrderId == w.Id && r.IsValid)
                    .Select(r => r.FormulaItemId).Distinct().Count(),
                db.FormulaItems.Count(i => i.FormulaId == w.FormulaId)))
            .ToList();
    }

    /// <summary>首页“今日”统计的原始数据：今天开始的工单（未删除）。</summary>
    public List<OrderDayInfo> OrdersStartedOn(DateTime day, int? operatorId = null)
    {
        var from = day.Date;
        var to = from.AddDays(1);
        using var db = _factory.CreateDbContext();
        var query = db.WorkOrders.Where(w => w.DeletedAt == null && w.StartTime >= from && w.StartTime < to);
        if (operatorId is int op) query = query.Where(w => w.OperatorId == op);
        return query
            .Select(w => new OrderDayInfo(w.StartTime, w.Status,
                db.WeightRecords.Any(r => r.WorkOrderId == w.Id && !r.IsValid)))
            .ToList();
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

public sealed record ActiveOrderRef(int Id, string? ProductionNo, string Operator);

public sealed record ActiveOrderInfo(int Id, int FormulaId, string FormulaCode, string FormulaName,
    int? OperatorId, string Operator, string? ProductionNo, DateTime StartTime, int DoneSteps, int TotalSteps);

public sealed record OrderSummary(DateTime StartTime, DateTime? EndTime, int StepCount, int ReworkCount);
