namespace MaterialMgmt.Core.Workflow;

public enum GaugeZone
{
    Below,
    In,
    Above
}

/// <summary>量程条的几何结果：所有位置均为 0~1 的横向比例。</summary>
public readonly record struct GaugeLayout(
    double DomainMin,
    double DomainMax,
    double Position,
    double RangeStart,
    double RangeEnd,
    GaugeZone Zone);

/// <summary>
/// 量程条位置计算（纯函数，便于测试）：显示范围 = 目标范围两侧各留 75% 跨度；
/// 指针位置夹在 [0,1]；区间判定与 WorkflowEngine 一致（含边界）。
/// </summary>
public static class RangeGaugeMath
{
    private const double PadRatio = 0.75;

    public static GaugeLayout Compute(double min, double max, double value)
    {
        if (max < min) (min, max) = (max, min);

        var span = max - min;
        if (span <= 0) span = Math.Max(Math.Abs(max) * 0.1, 1.0);
        var pad = span * PadRatio;

        var lo = min - pad;
        var hi = max + pad;
        // 重量不会为负：目标下限非负时，显示范围从 0 开始
        if (min >= 0 && lo < 0) lo = 0;
        var width = hi - lo;

        double Pos(double v) => double.IsNaN(v) ? 0 : Math.Clamp((v - lo) / width, 0, 1);

        var zone = double.IsNaN(value) || value < min
            ? GaugeZone.Below
            : value > max ? GaugeZone.Above : GaugeZone.In;

        return new GaugeLayout(lo, hi, Pos(value), Pos(min), Pos(max), zone);
    }
}
