namespace MaterialMgmt.Infrastructure.Hardware;

/// <summary>
/// 现场设备的字节和分发。ScaleWorker 读天平，BarcodeSerialService 收串口枪，
/// BarcodeInputService 把键盘楔入拼成一条码，BarcodeRouter 决定这条码交给哪个页面。
/// 连接和重连的定时器在 App 的 DeviceHub，不在这里。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "天平协议、扫码分帧、扫码分发。";
}
