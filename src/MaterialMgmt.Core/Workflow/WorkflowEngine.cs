using MaterialMgmt.Core.Entities;

namespace MaterialMgmt.Core.Workflow;

/// <summary>
/// 称重流程状态机（对齐 Python WorkflowEngine）。
/// 确认时返回 PendingWeightRecord，由调用方持久化；不直接依赖 DbContext。
/// </summary>
public sealed class WorkflowEngine
{
    private readonly List<FormulaItem> _items;
    private const double ZeroWeightEpsilon = 0.005;
    private string _scannedCode = "";
    private double _lastWeight;
    private bool _hasEffectiveWeight;

    public WorkflowEngine(IEnumerable<FormulaItem> items, int workOrderId, int startIndex = 0)
    {
        _items = items.OrderBy(i => i.Sequence).ToList();
        WorkOrderId = workOrderId;
        StepIndex = startIndex;
        State = startIndex >= _items.Count ? WeighState.Complete : WeighState.WaitingScan;
    }

    public int WorkOrderId { get; }
    public int StepIndex { get; private set; }
    public WeighState State { get; private set; }
    public IReadOnlyList<FormulaItem> Items => _items;
    public FormulaItem? CurrentItem => StepIndex < _items.Count ? _items[StepIndex] : null;
    public int CompletedCount => StepIndex;
    public int TotalCount => _items.Count;
    public bool IsComplete => State == WeighState.Complete;

    /// <summary>从已有工单恢复：第一个尚无有效 weight_records 的步骤。</summary>
    public static WorkflowEngine Resume(WorkOrder workOrder, IEnumerable<FormulaItem> items)
    {
        var completedIds = workOrder.Records
            .Where(r => r.IsValid)
            .Select(r => r.FormulaItemId)
            .ToHashSet();

        var sorted = items.OrderBy(i => i.Sequence).ToList();
        var start = 0;
        var found = false;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (!completedIds.Contains(sorted[i].Id))
            {
                start = i;
                found = true;
                break;
            }
        }
        if (!found) start = sorted.Count;

        return new WorkflowEngine(sorted, workOrder.Id, start);
    }

    public StepStatus OnBarcode(string code)
    {
        // 称重过程中允许重扫纠错：回到等待扫码再比对
        if (State is WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk)
        {
            _scannedCode = "";
            _hasEffectiveWeight = false;
            _lastWeight = 0;
            State = WeighState.WaitingScan;
        }

        if (State != WeighState.WaitingScan || CurrentItem is null)
            return new StepStatus(State, "");

        var scanned = NormalizeBarcode(code);
        var expected = NormalizeBarcode(CurrentItem.Material.Code);
        if (!string.Equals(scanned, expected, StringComparison.OrdinalIgnoreCase))
        {
            return new StepStatus(
                WeighState.WaitingScan,
                $"原料错误：当前步骤不匹配\n应扫：{CurrentItem.Material.Code}（{CurrentItem.Material.Name}）\n实扫：{code.Trim()}",
                IsError: true);
        }

        _scannedCode = scanned;
        _hasEffectiveWeight = false;
        State = WeighState.Weighing;
        return new StepStatus(
            WeighState.Weighing,
            $"请将原料放上秤盘\n目标范围：{CurrentItem.WeightMin} ~ {CurrentItem.WeightMax} g\n读数稳定且合格后，点击确认或按空格键");
    }

    public static string NormalizeBarcode(string code) => (code ?? "").Trim();

    public StepStatus OnWeightUpdate(double weight, bool isStable)
    {
        if (State is not (WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk))
            return new StepStatus(State, "");

        _lastWeight = weight;
        var item = CurrentItem;
        if (item is null) return new StepStatus(State, "");

        // 未稳定：只提示稳定中，不做合格判定
        if (!isStable)
        {
            State = WeighState.Weighing;
            return new StepStatus(WeighState.Weighing, "稳定中，请勿移动…");
        }

        if (!_hasEffectiveWeight)
        {
            if (Math.Abs(weight) <= ZeroWeightEpsilon)
            {
                State = WeighState.Weighing;
                return new StepStatus(WeighState.Weighing, "等待称重，请放置当前原料…");
            }
            _hasEffectiveWeight = true;
        }

        var inRange = item.WeightMin <= weight && weight <= item.WeightMax;

        if (inRange)
        {
            State = WeighState.WeightOk;
            return new StepStatus(WeighState.WeightOk, "读数已稳定且在范围内，请确认（空格键也可）");
        }

        State = WeighState.OutOfRange;
        var diff = weight > item.WeightMax ? weight - item.WeightMax : item.WeightMin - weight;
        var direction = weight > item.WeightMax ? "超出上限" : "低于下限";
        return new StepStatus(
            WeighState.OutOfRange,
            $"读数已稳定：{direction} {diff:F3} g\n请调整用量后等待再次稳定",
            IsError: true);
    }

    /// <summary>
    /// 确认称重。hasExistingValidRecord 由调用方查询后传入（同工单同步骤已有有效记录则拒绝）。
    /// </summary>
    public (StepStatus Status, PendingWeightRecord? Record) ConfirmWeight(bool hasExistingValidRecord)
    {
        if (State != WeighState.WeightOk || CurrentItem is null)
            return (new StepStatus(State, "当前状态无法确认"), null);

        if (hasExistingValidRecord)
        {
            return (new StepStatus(State, "该步骤已存在有效称重记录，请勿重复确认。", IsError: true), null);
        }

        var item = CurrentItem;
        var pending = new PendingWeightRecord
        {
            WorkOrderId = WorkOrderId,
            FormulaItemId = item.Id,
            BarcodeScanned = _scannedCode,
            ActualWeight = _lastWeight,
            Timestamp = DateTime.Now
        };

        StepIndex++;
        _scannedCode = "";
        _hasEffectiveWeight = false;

        if (StepIndex >= _items.Count)
        {
            State = WeighState.Complete;
            return (new StepStatus(WeighState.Complete, "🎉 所有步骤已完成！"), pending);
        }

        var next = _items[StepIndex];
        State = WeighState.WaitingScan;
        return (new StepStatus(
            WeighState.WaitingScan,
            $"步骤 {StepIndex + 1}/{TotalCount}\n请扫描 【{next.Material.Name}】 的条码（{next.Material.Code}）\n目标：{next.WeightMin} ~ {next.WeightMax} g"), pending);
    }
}
