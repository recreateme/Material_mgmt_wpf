namespace MaterialMgmt.Core.Orders;

/// <summary>开工单时填写的工艺参数。</summary>
public sealed class WorkOrderParams
{
    public double Temperature { get; init; }
    public double Humidity { get; init; }
    public string ProductionNo { get; init; } = "";
    public double? D50 { get; init; }
    public bool MixingException { get; init; }
    public string? MixingExceptionNote { get; init; }
    public string BaseFilm { get; init; } = "";
    public string? BatchNo { get; init; }
    public double? Thickness { get; init; }
    public bool CoatingException { get; init; }
    public string? CoatingExceptionNote { get; init; }
}
