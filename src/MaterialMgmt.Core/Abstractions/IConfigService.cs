using MaterialMgmt.Core.Hardware;

namespace MaterialMgmt.Core.Abstractions;

public interface IConfigService
{
    /// <summary>电子天平 COM。</summary>
    string SerialPort { get; set; }
    int SerialBaud { get; }
    bool AutoConnect { get; set; }

    /// <summary>扫码枪 USB COM（与天平口分离）。</summary>
    string ScannerPort { get; set; }
    int ScannerBaud { get; set; }
    ScannerInputMode ScannerMode { get; set; }
    bool ScannerEnabled { get; set; }

    (int Width, int Height) WindowSize { get; }
    bool WindowMaximized { get; }
    void SaveWindowSize(int width, int height, bool maximized);
    void Reload();
}
