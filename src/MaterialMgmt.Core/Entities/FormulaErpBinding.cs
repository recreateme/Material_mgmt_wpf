namespace MaterialMgmt.Core.Entities;

/// <summary>ERP 映射预留（界面隐藏，表保留）。</summary>
public class FormulaErpBinding
{
    public int Id { get; set; }
    public int FormulaId { get; set; }
    public string ErpTypeCode { get; set; } = "";
    public string? ErpLabel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Formula Formula { get; set; } = null!;
}
