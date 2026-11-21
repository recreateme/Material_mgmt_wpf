using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Services;

/// <summary>设备状态的显示文字与颜色。状态栏和首页共用，保证说法一致。</summary>
public readonly record struct DeviceStatus(string Text, Color Color, bool NeedsAttention);

public static class DeviceStatusText
{
    public static DeviceStatus Scale(DeviceHub d)
    {
        if (d.IsScaleConnected)
            return new DeviceStatus($"已连接（{d.ConnectedPort}）", UiTheme.Success, false);
        if (d.IsDiscovering || d.ScaleLastError.Contains("识别", StringComparison.Ordinal))
            return new DeviceStatus(d.ScaleLastError, UiTheme.Warning, false);
        var text = string.IsNullOrEmpty(d.ScaleLastError) ? "未连接" : $"未连接 — {d.ScaleLastError}";
        return new DeviceStatus(text, UiTheme.Danger, true);
    }

    public static DeviceStatus Scanner(DeviceHub d, bool exclusive = false)
    {
        var label = d.ScannerSourceLabel;
        var suffix = exclusive ? " · 称量独占" : "";
        var ok = (d.IsScannerSerialConnected
                  || label.Contains("HID", StringComparison.Ordinal)
                  || label.Contains("关闭", StringComparison.Ordinal))
                 && string.IsNullOrEmpty(d.ScannerLastError);

        string text;
        if (!string.IsNullOrEmpty(d.LastScanCode) && d.IsScannerListening)
            text = $"最近扫码 {d.LastScanCode}（{label}）";
        else if (d.IsScannerSerialConnected)
            text = $"已连接（{label}）";
        else if (!string.IsNullOrEmpty(d.ScannerLastError))
            text = $"{d.ScannerLastError}（{label}）";
        else
            text = d.IsScannerListening ? $"监听中（{label}）" : $"空闲（{label}）";

        var color = ok ? UiTheme.Success : d.IsScannerListening ? UiTheme.Warning : UiTheme.Danger;
        return new DeviceStatus(text + suffix, color, !ok);
    }
}
