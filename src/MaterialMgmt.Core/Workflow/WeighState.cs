namespace MaterialMgmt.Core.Workflow;

/// <summary>
/// 称量一步里的状态。只由 <see cref="WorkflowEngine"/> 的方法改变，界面不要自己改这个值。
/// </summary>
public enum WeighState
{
    /// <summary>等待扫描当前步骤的原料码。</summary>
    WaitingScan,
    /// <summary>已扫对，正在等秤的读数稳定。</summary>
    Weighing,
    /// <summary>读数已稳定，但不在配方上下限内，不能确认。</summary>
    OutOfRange,
    /// <summary>读数已稳定且在范围内，可以确认。</summary>
    WeightOk,
    /// <summary>全部步骤都已确认。</summary>
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
