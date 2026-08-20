namespace MaterialMgmt.Core.Workflow;

public enum WeighState
{
    WaitingScan,
    Weighing,
    OutOfRange,
    WeightOk,
    Complete
}

public sealed record StepStatus(WeighState State, string Message, bool IsError = false);

/// <summary>确认称重时产出的待持久化记录。</summary>
public sealed class PendingWeightRecord
{
    public int WorkOrderId { get; init; }
    public int FormulaItemId { get; init; }
    public string BarcodeScanned { get; init; } = "";
    public double ActualWeight { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
