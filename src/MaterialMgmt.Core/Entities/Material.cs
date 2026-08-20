namespace MaterialMgmt.Core.Entities;

/// <summary>原材料主数据。业务唯一码格式：MAT-xxxxxx。</summary>
public class Material
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<FormulaItem> FormulaItems { get; set; } = new List<FormulaItem>();
}
