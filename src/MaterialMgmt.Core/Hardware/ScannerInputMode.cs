namespace MaterialMgmt.Core.Hardware;

/// <summary>扫码枪输入模式。</summary>
public enum ScannerInputMode
{
    /// <summary>有扫码 COM 配置则用串口，否则 HID。</summary>
    Auto = 0,
    /// <summary>仅 USB COM 串口。</summary>
    Serial = 1,
    /// <summary>仅键盘楔入 HID。</summary>
    Hid = 2
}
