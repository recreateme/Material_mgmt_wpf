using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Workflow;

namespace MaterialMgmt.Tests.Workflow;

public class WorkflowEngineTests
{
    private static FormulaItem Item(int id, string code, string name, double min, double max, int seq)
    {
        return new FormulaItem
        {
            Id = id,
            Sequence = seq,
            WeightMin = min,
            WeightMax = max,
            Material = new Material { Id = id, Code = code, Name = name }
        };
    }

    [Fact]
    public void Barcode_Mismatch_StaysWaiting()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1, 2, 1) }, 10);
        var st = eng.OnBarcode("MAT-999999");
        Assert.True(st.IsError);
        Assert.Contains("原料错误", st.Message);
        Assert.Equal(WeighState.WaitingScan, eng.State);
    }

    [Fact]
    public void HappyPath_Complete()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1.0, 2.0, 1) }, 10);
        Assert.Equal(WeighState.Weighing, eng.OnBarcode("MAT-000001").State);
        Assert.Equal(WeighState.Weighing, eng.OnWeightUpdate(0, false).State); // 稳定中
        Assert.Equal(WeighState.Weighing, eng.OnWeightUpdate(0, true).State);  // 仍接近 0，等待称重
        Assert.Equal(WeighState.Weighing, eng.OnWeightUpdate(1.5, false).State);
        Assert.Equal(WeighState.WeightOk, eng.OnWeightUpdate(1.5, true).State);
        var (st, pending) = eng.ConfirmWeight(false);
        Assert.NotNull(pending);
        Assert.Equal(WeighState.Complete, st.State);
        Assert.True(eng.IsComplete);
    }

    [Fact]
    public void Unstable_DoesNotJudge()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1.0, 2.0, 1) }, 10);
        eng.OnBarcode("MAT-000001");
        eng.OnWeightUpdate(1.5, false);
        var st = eng.OnWeightUpdate(3.0, false); // 超范围但不稳定 → 不判定
        Assert.Equal(WeighState.Weighing, st.State);
        Assert.Contains("稳定中", st.Message);
    }

    [Fact]
    public void OutOfRange_CannotConfirm()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1.0, 2.0, 1) }, 10);
        eng.OnBarcode("MAT-000001");
        var st = eng.OnWeightUpdate(3.0, true);
        Assert.Equal(WeighState.OutOfRange, st.State);
        Assert.True(st.IsError);
        var (confirm, pending) = eng.ConfirmWeight(false);
        Assert.Null(pending);
        Assert.Contains("无法确认", confirm.Message);
    }

    [Fact]
    public void Resume_SkipsCompleted()
    {
        var items = new[]
        {
            Item(1, "MAT-000001", "A", 1, 2, 1),
            Item(2, "MAT-000002", "B", 1, 2, 2)
        };
        var wo = new WorkOrder
        {
            Id = 5,
            Records =
            {
                new WeightRecord { FormulaItemId = 1, IsValid = true, ActualWeight = 1.5, BarcodeScanned = "MAT-000001" }
            }
        };
        var eng = WorkflowEngine.Resume(wo, items);
        Assert.Equal(1, eng.StepIndex);
        Assert.Equal(WeighState.WaitingScan, eng.State);
        Assert.Equal("MAT-000002", eng.CurrentItem!.Material.Code);
    }

    [Fact]
    public void Rescan_DuringWeighing_AllowsCorrection()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1.0, 2.0, 1) }, 10);
        Assert.Equal(WeighState.Weighing, eng.OnBarcode("MAT-000001").State);
        eng.OnWeightUpdate(1.5, true);
        Assert.Equal(WeighState.WeightOk, eng.State);

        var bad = eng.OnBarcode("MAT-999999");
        Assert.True(bad.IsError);
        Assert.Equal(WeighState.WaitingScan, eng.State);

        Assert.Equal(WeighState.Weighing, eng.OnBarcode("MAT-000001").State);
    }

    [Fact]
    public void Barcode_TrimsWhitespace()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1, 2, 1) }, 10);
        Assert.Equal(WeighState.Weighing, eng.OnBarcode("  MAT-000001  ").State);
    }

    [Fact]
    public void RewindToStep_ResetsToWaitingScan()
    {
        var items = new[]
        {
            Item(1, "MAT-000001", "A", 1, 2, 1),
            Item(2, "MAT-000002", "B", 1, 2, 2)
        };
        var eng = new WorkflowEngine(items, 10);
        eng.OnBarcode("MAT-000001");
        eng.OnWeightUpdate(1.5, true);
        eng.ConfirmWeight(false);
        Assert.Equal(1, eng.StepIndex);

        var st = eng.RewindToStep(0);
        Assert.Equal(0, eng.StepIndex);
        Assert.Equal(WeighState.WaitingScan, st.State);
        Assert.Contains("重新称量", st.Message);
    }

    [Fact]
    public void SkipWhileCompleted_JumpsPastValidSteps()
    {
        var items = new[]
        {
            Item(1, "MAT-000001", "A", 1, 2, 1),
            Item(2, "MAT-000002", "B", 1, 2, 2),
            Item(3, "MAT-000003", "C", 1, 2, 3)
        };
        var eng = new WorkflowEngine(items, 10, startIndex: 0);
        // 模拟回称第 1 步后确认，第 2 步仍有有效记录
        eng.OnBarcode("MAT-000001");
        eng.OnWeightUpdate(1.5, true);
        eng.ConfirmWeight(false);
        Assert.Equal(1, eng.StepIndex);

        var valid = new HashSet<int> { 2 }; // 第 2 步仍有效
        var st = eng.SkipWhileCompleted(id => valid.Contains(id));
        Assert.Equal(2, eng.StepIndex);
        Assert.Equal(WeighState.WaitingScan, st.State);
        Assert.Equal("MAT-000003", eng.CurrentItem!.Material.Code);
    }

    [Fact]
    public void ReadingLost_RevokesWeightOk_AndBlocksConfirm()
    {
        var eng = new WorkflowEngine(new[] { Item(1, "MAT-000001", "A", 1.0, 2.0, 1) }, 10);
        eng.OnBarcode("MAT-000001");
        eng.OnWeightUpdate(1.5, true);
        Assert.Equal(WeighState.WeightOk, eng.State);

        var st = eng.OnReadingLost();
        Assert.True(st.IsError);
        Assert.Equal(WeighState.Weighing, eng.State);
        var (_, pending) = eng.PrepareConfirm(false);
        Assert.Null(pending); // 读数过期后不可确认

        // 读数恢复且稳定合格后才能再次确认
        Assert.Equal(WeighState.WeightOk, eng.OnWeightUpdate(1.5, true).State);
    }

    [Fact]
    public void PrepareConfirm_DoesNotAdvance_UntilCommit()
    {
        var items = new[] { Item(1, "MAT-000001", "A", 1, 2, 1), Item(2, "MAT-000002", "B", 1, 2, 2) };
        var eng = new WorkflowEngine(items, 10);
        eng.OnBarcode("MAT-000001");
        eng.OnWeightUpdate(1.5, true);

        var (_, pending) = eng.PrepareConfirm(false);
        Assert.NotNull(pending);
        Assert.Equal(1.5, pending!.ActualWeight);
        // 模拟落库失败：状态机未前进，可直接重试
        Assert.Equal(0, eng.StepIndex);
        Assert.Equal(WeighState.WeightOk, eng.State);

        var next = eng.CommitConfirm();
        Assert.Equal(1, eng.StepIndex);
        Assert.Equal(WeighState.WaitingScan, next.State);
    }
}
