using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// 窗口底部设备状态栏（主窗与称量窗共用）。设备异常时整条变为警告底色，点击对应设备即重连。
/// 设备事件在 Loaded 时订阅、Unloaded 时取消，窗口关闭后不会残留订阅。
/// </summary>
public sealed class DeviceStatusBar : Border
{
    private readonly DeviceHub _devices;
    private readonly IConfigService _config;
    private readonly Func<bool> _exclusive;
    private readonly TextBlock _scale = Segment();
    private readonly TextBlock _scanner = Segment();
    private bool _scaleNeedsAttention;
    private bool _scannerNeedsAttention;
    private DateTime _scaleRetryUntil = DateTime.MinValue;
    private static readonly TimeSpan ScaleRetryWindow = TimeSpan.FromSeconds(8);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public DeviceStatusBar(DeviceHub devices, IConfigService config, Func<bool>? exclusive = null, string? note = null)
    {
        _devices = devices;
        _config = config;
        _exclusive = exclusive ?? (() => false);

        BorderBrush = UiTheme.Brush(UiTheme.Border);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(14, 7, 14, 7);

        _scale.Margin = new Thickness(0, 0, 18, 0);
        _scale.MouseLeftButtonUp += (_, _) => ReconnectScale();
        _scanner.MouseLeftButtonUp += (_, _) => ReconnectScanner();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_scale);
        row.Children.Add(_scanner);
        if (!string.IsNullOrEmpty(note))
        {
            row.Children.Add(new TextBlock
            {
                Text = "  ·  " + note,
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.Brush(UiTheme.Muted),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Child = row;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _devices.ScaleStatusChanged += OnDeviceChanged;
            _devices.ScannerStatusChanged += OnDeviceChanged;
            _timer.Start();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _devices.ScaleStatusChanged -= OnDeviceChanged;
            _devices.ScannerStatusChanged -= OnDeviceChanged;
        };
        Refresh();
    }

    public void Refresh()
    {
        var scale = DeviceStatusText.Scale(_devices);
        // 重连是异步的：结果出来前显示“正在重连”，不让旧的错误信息闪回
        if (!_devices.IsScaleConnected && !_devices.IsDiscovering && DateTime.Now < _scaleRetryUntil)
            scale = new DeviceStatus("正在重连…", UiTheme.Warning, false);
        else if (_devices.IsScaleConnected)
            _scaleRetryUntil = DateTime.MinValue;
        var scanner = DeviceStatusText.Scanner(_devices, _exclusive());
        _scaleNeedsAttention = scale.NeedsAttention;
        _scannerNeedsAttention = scanner.NeedsAttention;

        Apply(_scale, "电子秤", scale, "点击重连电子秤");
        Apply(_scanner, "扫码枪", scanner, "点击重新连接扫码枪");
        Background = UiTheme.Brush(scale.NeedsAttention || scanner.NeedsAttention ? UiTheme.WarningSoft : UiTheme.PanelAlt);
    }

    private static void Apply(TextBlock tb, string name, DeviceStatus s, string action)
    {
        tb.Text = $"● {name}：{s.Text}" + (s.NeedsAttention ? "  ↻ 点击重连" : "");
        tb.Foreground = UiTheme.Brush(s.Color);
        tb.Cursor = s.NeedsAttention ? Cursors.Hand : null;
        tb.ToolTip = s.NeedsAttention ? action : null;
    }

    private void ReconnectScale()
    {
        if (!_scaleNeedsAttention) return;
        _scaleRetryUntil = DateTime.Now + ScaleRetryWindow;
        _devices.ReconnectScale(_config);
        Refresh();
    }

    private void ReconnectScanner()
    {
        if (!_scannerNeedsAttention) return;
        _devices.ApplyScannerConfig(_config);
        Refresh();
        if (_scannerNeedsAttention)
            UiFeedback.Warn("扫码枪仍未连接，请检查连线或在「系统设置」中确认扫码口。");
    }

    private void OnDeviceChanged() => Dispatcher.BeginInvoke(Refresh);

    private static TextBlock Segment() => new()
    {
        FontSize = UiTheme.FontCaption,
        VerticalAlignment = VerticalAlignment.Center
    };
}
