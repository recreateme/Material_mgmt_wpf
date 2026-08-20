namespace MaterialMgmt.Core.Entities;

/// <summary>称量工单。status: in_progress / completed / cancelled。</summary>
public class WorkOrder
{
    public int Id { get; set; }
    public int FormulaId { get; set; }
    public int? OperatorId { get; set; }
    public string Operator { get; set; } = "";
    public string? ErpTypeCode { get; set; }
    public string? ExternalOrderNo { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "in_progress";

    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public string? ProductionNo { get; set; }
    public double? D50 { get; set; }
    public bool? MixingException { get; set; }
    public string? BaseFilm { get; set; }
    public string? BatchNo { get; set; }
    public double? Thickness { get; set; }
    public bool? CoatingException { get; set; }
    public string? MixingExceptionNote { get; set; }
    public string? CoatingExceptionNote { get; set; }

    public Formula Formula { get; set; } = null!;
    public User? User { get; set; }
    public ICollection<WeightRecord> Records { get; set; } = new List<WeightRecord>();
}
