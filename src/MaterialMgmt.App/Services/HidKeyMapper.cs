using System.Windows.Input;

namespace MaterialMgmt.App.Services;

/// <summary>键盘模式（HID）扫码枪：把按键转成字符交给 <see cref="DeviceHub.Barcode"/> 聚合。主窗与称量窗共用。</summary>
public static class HidKeyMapper
{
    public static void Forward(DeviceHub devices, KeyEventArgs e)
    {
        if (!devices.ShouldAcceptHidKeys) return;
        var enter = e.Key is Key.Enter or Key.Return;
        devices.Barcode.OnKey(enter, enter ? null : ToText(e.Key, Keyboard.Modifiers));
    }

    /// <summary>仅处理扫码枪常见的可打印字符（数字、字母、- _ .），其余返回 null。</summary>
    public static string? ToText(Key key, ModifierKeys modifiers)
    {
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key >= Key.A && key <= Key.Z)
        {
            var c = (char)('A' + (key - Key.A));
            return shift ? c.ToString() : char.ToLowerInvariant(c).ToString();
        }
        if (key is Key.OemMinus or Key.Subtract) return shift ? "_" : "-";
        if (key == Key.OemPeriod) return ".";
        return null;
    }
}
