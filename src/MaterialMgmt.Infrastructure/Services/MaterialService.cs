using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

/// <summary>
/// 原材料的增删改。校验失败抛 <see cref="InvalidOperationException"/>（编码重复 / 格式错为 <see cref="CodeError"/>），
/// 消息可直接给用户看。
/// </summary>
public sealed class MaterialService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;

    public MaterialService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    public List<Material> All()
    {
        using var db = _factory.CreateDbContext();
        return db.Materials.AsNoTracking().OrderBy(m => m.Code).ToList();
    }

    /// <summary>新增时自动编码的预览（实际编码在保存时再分配，避免并发冲突）。</summary>
    public string PreviewNextCode()
    {
        using var db = _factory.CreateDbContext();
        var last = db.Materials.OrderByDescending(m => m.Id).Select(m => (int?)m.Id).FirstOrDefault();
        return CodeService.PreviewNext(CodeKind.Material, last);
    }

    /// <summary>新增原材料。<paramref name="manualCode"/> 为 null 时自动分配编码。</summary>
    public Material Create(string name, string? description, string? manualCode)
    {
        using var db = _factory.CreateDbContext();
        var entity = new Material
        {
            Name = RequireName(name),
            Description = NullIfBlank(description),
            CreatedAt = DateTime.Now,
            Code = manualCode is null ? NextCode(db) : UniqueCode(db, manualCode, null)
        };
        db.Materials.Add(entity);
        db.SaveChanges();
        return entity;
    }

    /// <summary>编辑原材料。<paramref name="manualCode"/> 为 null 时保留原编码。</summary>
    public Material Update(int id, string name, string? description, string? manualCode)
    {
        using var db = _factory.CreateDbContext();
        var entity = db.Materials.FirstOrDefault(m => m.Id == id)
                     ?? throw new InvalidOperationException("该原材料已不存在，请刷新列表");
        entity.Name = RequireName(name);
        entity.Description = NullIfBlank(description);
        if (manualCode is not null)
            entity.Code = UniqueCode(db, manualCode, id);
        db.SaveChanges();
        return entity;
    }

    /// <summary>给定原材料中已被配方步骤引用的那些（这些不能删除）。</summary>
    public List<Material> ReferencedByFormulas(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        using var db = _factory.CreateDbContext();
        return db.Materials.AsNoTracking()
            .Where(m => list.Contains(m.Id) && db.FormulaItems.Any(i => i.MaterialId == m.Id))
            .OrderBy(m => m.Code)
            .ToList();
    }

    /// <summary>批量删除，单事务。任一项被配方引用时整批不删并抛异常。返回删除的原材料。</summary>
    public List<Material> DeleteMany(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        if (db.FormulaItems.Any(i => list.Contains(i.MaterialId)))
            throw new InvalidOperationException("所选原材料中有被配方引用的，未删除任何项");
        var entities = db.Materials.Where(m => list.Contains(m.Id)).ToList();
        db.Materials.RemoveRange(entities);
        db.SaveChanges();
        return entities;
    }

    private static string RequireName(string name)
        => string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("请填写名称") : name.Trim();

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string NextCode(MaterialMgmtDbContext db)
    {
        var nextId = (db.Materials.Select(m => (int?)m.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Material, nextId, (c, _) => !db.Materials.Any(m => m.Code == c));
    }

    private static string UniqueCode(MaterialMgmtDbContext db, string code, int? selfId)
        => CodeService.AssertUnique(Array.Empty<string>(), code, selfId,
            db.Materials.Select(m => new ValueTuple<int, string>(m.Id, m.Code)).AsEnumerable());
}
