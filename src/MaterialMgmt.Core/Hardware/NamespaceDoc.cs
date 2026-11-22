namespace MaterialMgmt.Core.Hardware;

/// <summary>
/// 设备侧只放「名字」：扫码来源 serial / hid / simulated，以及扫码枪模式（自动、仅串口、仅键盘）。
/// 开串口、读字节在 Infrastructure.Hardware。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "扫码来源和扫码模式的常量，不含串口代码。";
}
