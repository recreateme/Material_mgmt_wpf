namespace MaterialMgmt.Core.Entities;

/// <summary>配方称量流程项：顺序、材料、克重上下限。</summary>
public class FormulaItem
{
    public int Id { get; set; }
    public int FormulaId { get; set; }
    public int MaterialId { get; set; }
    public int Sequence { get; set; }
    public double WeightMin { get; set; }
    public double WeightMax { get; set; }
    public string? Notes { get; set; }

    public Formula Formula { get; set; } = null!;
    public Material Material { get; set; } = null!;
    public ICollection<WeightRecord> WeightRecords { get; set; } = new List<WeightRecord>();
}
