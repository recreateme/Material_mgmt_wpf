using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class FormulaService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;

    public FormulaService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>复制配方及全部步骤，返回新配方 Id。</summary>
    public int Clone(int sourceFormulaId, string? newName, string? createdBy)
    {
        using var db = _factory.CreateDbContext();
        var src = db.Formulas.Include(f => f.Items)
                       .FirstOrDefault(f => f.Id == sourceFormulaId)
                   ?? throw new InvalidOperationException($"配方 {sourceFormulaId} 不存在");

        var name = string.IsNullOrWhiteSpace(newName)
            ? FormulaClone.DefaultCopyName(src.Name)
            : newName.Trim();

        var nextId = (db.Formulas.Select(x => (int?)x.Id).Max() ?? 0) + 1;
        var code = CodeService.AssignAuto(CodeKind.Formula, nextId,
            (c, _) => !db.Formulas.Any(x => x.Code == c));

        var steps = FormulaClone.SnapshotSteps(
            src.Items.Select(i => (i.MaterialId, i.Sequence, i.WeightMin, i.WeightMax, i.Notes)));

        var copy = new Formula
        {
            Code = code,
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
        return copy.Id;
    }
}
