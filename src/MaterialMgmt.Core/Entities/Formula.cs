namespace MaterialMgmt.Core.Entities;

/// <summary>配方主数据。业务唯一码格式：FML-xxxxxx。</summary>
public class Formula
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;

    public ICollection<FormulaItem> Items { get; set; } = new List<FormulaItem>();
    public ICollection<FormulaErpBinding> ErpBindings { get; set; } = new List<FormulaErpBinding>();
    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}
