namespace MaterialMgmt.Core.Abstractions;

public interface IConfigService
{
    string SerialPort { get; set; }
    int SerialBaud { get; }
    bool AutoConnect { get; set; }
    (int Width, int Height) WindowSize { get; }
    bool WindowMaximized { get; }
    void SaveWindowSize(int width, int height, bool maximized);
    void Reload();
}
