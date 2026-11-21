using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

/// <summary>
/// 配方与配方步骤的增删改。校验失败抛 <see cref="InvalidOperationException"/>，消息可直接给用户看。
/// 步骤写操作都先复核“配方未被进行中工单引用”，并在单个事务内完成重新编号。
/// </summary>
public sealed class FormulaService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private readonly OrderService _orders;

    public FormulaService(IDbContextFactory<MaterialMgmtDbContext> factory, OrderService orders)
    {
        _factory = factory;
        _orders = orders;
    }

    /// <summary>全部配方（不含步骤）；<paramref name="activeOnly"/> 时只含启用的。</summary>
    public List<Formula> All(bool activeOnly = false)
    {
        using var db = _factory.CreateDbContext();
        var q = db.Formulas.AsNoTracking();
        if (activeOnly) q = q.Where(f => f.IsActive);
        return q.ToList();
    }

    public string PreviewNextCode()
    {
        using var db = _factory.CreateDbContext();
        var last = db.Formulas.OrderByDescending(f => f.Id).Select(f => (int?)f.Id).FirstOrDefault();
        return CodeService.PreviewNext(CodeKind.Formula, last);
    }

    /// <summary>新建空配方（启用）。<paramref name="manualCode"/> 为 null 时自动分配编码。</summary>
    public Formula Create(string name, string? manualCode, string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请填写名称");
        using var db = _factory.CreateDbContext();
        var f = new Formula
        {
            Name = name.Trim(),
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now,
            IsActive = true,
            Code = manualCode is null
                ? NextCode(db)
                : CodeService.AssertUnique(Array.Empty<string>(), manualCode, null,
                    db.Formulas.Select(x => new ValueTuple<int, string>(x.Id, x.Code)).AsEnumerable())
        };
        db.Formulas.Add(f);
        db.SaveChanges();
        return f;
    }

    /// <summary>复制配方及全部步骤，返回新配方。</summary>
    public Formula Clone(int sourceFormulaId, string? newName, string? createdBy)
    {
        using var db = _factory.CreateDbContext();
        var src = db.Formulas.Include(f => f.Items)
                       .FirstOrDefault(f => f.Id == sourceFormulaId)
                   ?? throw new InvalidOperationException($"配方 {sourceFormulaId} 不存在");

        var name = string.IsNullOrWhiteSpace(newName)
            ? FormulaClone.DefaultCopyName(src.Name)
            : newName.Trim();

        var steps = FormulaClone.SnapshotSteps(
            src.Items.Select(i => (i.MaterialId, i.Sequence, i.WeightMin, i.WeightMax, i.Notes)));

        var copy = new Formula
        {
            Code = NextCode(db),
            Name = name,
            Description = src.Description,
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now,
            IsActive = true
        };
        foreach (var s in steps)
        {
            copy.Items.Add(new FormulaItem
            {
                MaterialId = s.MaterialId,
                Sequence = s.Sequence,
                WeightMin = s.WeightMin,
                WeightMax = s.WeightMax,
                Notes = s.Notes
            });
        }

        db.Formulas.Add(copy);
        db.SaveChanges();
        return copy;
    }

    /// <summary>切换所给配方各自的启用状态，返回切换后的配方。</summary>
    public List<Formula> ToggleActive(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        var entities = db.Formulas.Where(f => list.Contains(f.Id)).ToList();
        foreach (var f in entities) f.IsActive = !f.IsActive;
        db.SaveChanges();
        return entities;
    }

    /// <summary>给定配方中已开过工单的那些（这些不能删除）。</summary>
    public List<Formula> WithWorkOrders(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        using var db = _factory.CreateDbContext();
        return db.Formulas.AsNoTracking()
            .Where(f => list.Contains(f.Id) && db.WorkOrders.Any(w => w.FormulaId == f.Id))
            .OrderBy(f => f.Code)
            .ToList();
    }

    /// <summary>删除配方及其步骤，单事务。任一配方开过工单（含已删除工单）时整批不删并抛异常。</summary>
    public List<Formula> DeleteMany(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        if (db.WorkOrders.Any(w => list.Contains(w.FormulaId)))
            throw new InvalidOperationException("所选配方中有已开过工单的，未删除任何项");
        var entities = db.Formulas.Include(f => f.Items).Where(f => list.Contains(f.Id)).ToList();
        foreach (var f in entities)
        {
            db.FormulaItems.RemoveRange(f.Items);
            db.Formulas.Remove(f);
        }
        db.SaveChanges();
        return entities;
    }

    /// <summary>配方及其步骤（含原料），步骤按序号排列；不存在时返回 null。</summary>
    public Formula? GetWithSteps(int formulaId)
    {
        using var db = _factory.CreateDbContext();
        var f = db.Formulas.AsNoTracking()
            .Include(x => x.Items).ThenInclude(i => i.Material)
            .FirstOrDefault(x => x.Id == formulaId);
        if (f is not null)
            f.Items = f.Items.OrderBy(i => i.Sequence).ToList();
        return f;
    }

    /// <summary>添加步骤：插到 <paramref name="afterItemId"/> 之后，null 或找不到时追加到末尾。返回新步骤 Id。</summary>
    public int AddStep(int formulaId, StepInput input, int? afterItemId)
    {
        _orders.EnsureFormulaStepsEditable(formulaId);
        using var db = _factory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();
        var ordered = OrderedStepIds(db, formulaId);
        var insertIndex = ordered.Count;
        if (afterItemId is int after && ordered.IndexOf(after) is var idx and >= 0)
            insertIndex = idx + 1;

        var entity = new FormulaItem
        {
            FormulaId = formulaId,
            MaterialId = input.MaterialId,
            Sequence = FormulaSequence.TempOffset * 2 + ordered.Count + 1,
            WeightMin = input.Min,
            WeightMax = input.Max,
            Notes = input.Notes
        };
        db.FormulaItems.Add(entity);
        db.SaveChanges();

        ordered.Insert(insertIndex, entity.Id);
        ApplySequence(db, ordered);
        tx.Commit();
        return entity.Id;
    }

    public void UpdateStep(int itemId, StepInput input)
    {
        using var db = _factory.CreateDbContext();
        var e = db.FormulaItems.FirstOrDefault(i => i.Id == itemId)
                ?? throw new InvalidOperationException("该步骤已不存在，请刷新");
        _orders.EnsureFormulaStepsEditable(e.FormulaId);
        e.MaterialId = input.MaterialId;
        e.WeightMin = input.Min;
        e.WeightMax = input.Max;
        e.Notes = input.Notes;
        db.SaveChanges();
    }

    /// <summary>按给定顺序重排配方的全部步骤；<paramref name="orderedIds"/> 必须恰好是该配方现有的步骤。</summary>
    public void Reorder(int formulaId, IReadOnlyList<int> orderedIds)
    {
        _orders.EnsureFormulaStepsEditable(formulaId);
        using var db = _factory.CreateDbContext();
        var current = OrderedStepIds(db, formulaId);
        if (current.Count != orderedIds.Count || !current.ToHashSet().SetEquals(orderedIds))
            throw new InvalidOperationException("步骤列表已被修改，请刷新后再调整顺序");
        using var tx = db.Database.BeginTransaction();
        ApplySequence(db, orderedIds);
        tx.Commit();
    }

    /// <summary>删除步骤并把剩余步骤重新编号为 1..n。已有称重记录的步骤不能删（抛异常，未删除任何项）。</summary>
    public void DeleteSteps(int formulaId, IEnumerable<int> itemIds)
    {
        _orders.EnsureFormulaStepsEditable(formulaId);
        var ids = itemIds.Distinct().ToList();
        if (_orders.StepsWithRecords(ids).Count > 0)
            throw new InvalidOperationException("所选步骤中有已产生称重记录的，不能删除");

        using var db = _factory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();
        db.FormulaItems.RemoveRange(db.FormulaItems.Where(i => i.FormulaId == formulaId && ids.Contains(i.Id)));
        db.SaveChanges();
        var remain = OrderedStepIds(db, formulaId);
        if (remain.Count > 0)
            ApplySequence(db, remain);
        tx.Commit();
    }

    private static List<int> OrderedStepIds(MaterialMgmtDbContext db, int formulaId)
        => db.FormulaItems.Where(i => i.FormulaId == formulaId).OrderBy(i => i.Sequence).Select(i => i.Id).ToList();

    /// <summary>(formula_id, sequence) 有唯一索引：先全部写临时序号，再写 1..n。</summary>
    private static void ApplySequence(MaterialMgmtDbContext db, IReadOnlyList<int> orderedIds)
    {
        var items = db.FormulaItems.Where(i => orderedIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var phases = FormulaSequence.BuildTwoPhase(orderedIds);
        foreach (var (id, seq) in phases.Take(orderedIds.Count))
            items[id].Sequence = seq;
        db.SaveChanges();
        foreach (var (id, seq) in phases.Skip(orderedIds.Count))
            items[id].Sequence = seq;
        db.SaveChanges();
    }

    private static string NextCode(MaterialMgmtDbContext db)
    {
        var nextId = (db.Formulas.Select(x => (int?)x.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Formula, nextId, (c, _) => !db.Formulas.Any(x => x.Code == c));
    }
}
