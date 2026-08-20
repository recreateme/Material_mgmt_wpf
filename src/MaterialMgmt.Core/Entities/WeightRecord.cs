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

    public WorkOrder WorkOrder { get; set; } = null!;
    public FormulaItem FormulaItem { get; set; } = null!;
}
