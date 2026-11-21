namespace MaterialMgmt.Core.Entities;

/// <summary>单步称重记录。</summary>
public class WeightRecord
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public int FormulaItemId { get; set; }
    public string BarcodeScanned { get; set; } = "";
    public double? TareWeight { get; set; }
    public double ActualWeight { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsValid { get; set; } = true;
    public string? Note { get; set; }

    // —— 称重当时的快照：配方/原料之后被修改，历史记录与导出仍按当时的限值与名称呈现 ——
    public double? WeightMinSnapshot { get; set; }
    public double? WeightMaxSnapshot { get; set; }
    public string? MaterialCodeSnapshot { get; set; }
    public string? MaterialNameSnapshot { get; set; }

    // —— 作废留痕：回称时记录作废时间、操作人与原因 ——
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>原料码的来源，取值见 <see cref="Hardware.ScanSources"/>；旧记录为空。</summary>
    public string? ScanSource { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public FormulaItem FormulaItem { get; set; } = null!;
}
