using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Workflow;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// 称量量程条：浅色轨道上标出目标范围（绿色区段），指针随实时重量移动；
/// 指针在范围内为绿色、范围外为红色、未稳定为橙色。位置计算见 <see cref="RangeGaugeMath"/>。
/// </summary>
public sealed class RangeGauge : FrameworkElement
{
    private double _min, _max;
    private double? _value;
    private bool _stable = true;
    private bool _hasRange;

    public RangeGauge()
    {
        Height = 54;
        MinWidth = 200;
        SnapsToDevicePixels = true;
    }

    /// <summary>设置目标范围；value 为 null 表示尚无读数（不画指针）。</summary>
    public void Update(double? min, double? max, double? value, bool stable)
    {
        _hasRange = min.HasValue && max.HasValue;
        _min = min ?? 0;
        _max = max ?? 0;
        _value = value;
        _stable = stable;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        if (w <= 0) return;

        const double trackTop = 14, trackH = 14;
        var pxPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var track = new Rect(0, trackTop, w, trackH);
        dc.DrawRoundedRectangle(UiTheme.Brush(UiTheme.PanelAlt), new Pen(UiTheme.Brush(UiTheme.Border), 1), track, 7, 7);

        if (!_hasRange) return;

        var g = RangeGaugeMath.Compute(_min, _max, _value ?? double.NaN);

        // 目标范围区段
        var x1 = g.RangeStart * w;
        var x2 = g.RangeEnd * w;
        var zone = new Rect(x1, trackTop, Math.Max(x2 - x1, 2), trackH);
        dc.DrawRoundedRectangle(UiTheme.Brush(UiTheme.WithAlpha(UiTheme.Success, 70)),
            new Pen(UiTheme.Brush(UiTheme.Success), 1.2), zone, 4, 4);

        // 范围两端标注
        DrawLabel(dc, Format(_min), x1, trackTop + trackH + 4, pxPerDip, w, UiTheme.Muted);
        DrawLabel(dc, Format(_max), x2, trackTop + trackH + 4, pxPerDip, w, UiTheme.Muted);

        // 指针
        if (_value is null) return;
        var color = !_stable ? UiTheme.WeightUnstable
            : g.Zone == GaugeZone.In ? UiTheme.WeightOk
            : UiTheme.WeightBad;
        var brush = UiTheme.Brush(color);
        var px = Math.Clamp(g.Position * w, 2, w - 2);

        dc.DrawRoundedRectangle(brush, null, new Rect(px - 2, trackTop - 4, 4, trackH + 8), 2, 2);
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(px, trackTop - 3), true, true);
            ctx.LineTo(new Point(px - 6, trackTop - 12), true, false);
            ctx.LineTo(new Point(px + 6, trackTop - 12), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(brush, null, tri);
    }

    private void DrawLabel(DrawingContext dc, string text, double centerX, double y, double pxPerDip, double width, Color color)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 12, UiTheme.Brush(color), pxPerDip);
        var x = Math.Clamp(centerX - ft.Width / 2, 0, Math.Max(0, width - ft.Width));
        dc.DrawText(ft, new Point(x, y));
    }

    private static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
