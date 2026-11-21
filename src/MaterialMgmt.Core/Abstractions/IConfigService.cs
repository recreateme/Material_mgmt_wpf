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

    /// <summary>称量页提示音（扫码错误、合格、超范围、读数中断）。</summary>
    bool SoundEnabled { get; set; }

    /// <summary>读写任意配置项（如工艺参数对话框记住上次填写的值）。</summary>
    string GetValue(string section, string key, string fallback);
    void SetValue(string section, string key, string value);

    (int Width, int Height) WindowSize { get; }
    bool WindowMaximized { get; }
    void SaveWindowSize(int width, int height, bool maximized);
    void Reload();
}
