using System.Windows;
using System.Windows.Controls;

namespace MaterialMgmt.App.Services;

/// <summary>将全局扫码结果填入筛选框或弹窗编码框。</summary>
public static class BarcodeFillHelper
{
    public static void FillFilter(TextBox filter, string code)
    {
        var normalized = DeviceHub.NormalizeCode(code);
        if (string.IsNullOrEmpty(normalized)) return;
        filter.Text = normalized;
        filter.CaretIndex = filter.Text.Length;
    }

    /// <summary>自动勾选手工指定编码并写入编码框。</summary>
    public static void ApplyToManualCode(CheckBox manual, TextBox code, string raw)
    {
        var normalized = DeviceHub.NormalizeCode(raw);
        if (string.IsNullOrEmpty(normalized)) return;
        if (manual.IsChecked != true)
            manual.IsChecked = true;
        code.IsEnabled = true;
        code.Text = normalized;
        code.CaretIndex = code.Text.Length;
        code.Focus();
    }

    /// <summary>弹窗显示期间订阅扫码，关闭时自动取消。</summary>
    public static void AttachDialog(Window dialog, DeviceHub devices, CheckBox manual, TextBox code)
    {
        void Handler(string c, bool _) => ApplyToManualCode(manual, code, c);
        dialog.Loaded += OnLoaded;
        dialog.Closed += OnClosed;

        void OnLoaded(object? _, RoutedEventArgs __) => devices.BarcodeRouted += Handler;
        void OnClosed(object? _, EventArgs __)
        {
            devices.BarcodeRouted -= Handler;
            dialog.Loaded -= OnLoaded;
            dialog.Closed -= OnClosed;
        }
    }
}
