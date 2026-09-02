using System.IO.Ports;
using System.Text;

namespace MaterialMgmt.Infrastructure.Hardware;

/// <summary>
/// USB COM 扫码枪整帧接收（按 CR/LF 分帧）。
/// 与电子秤 ScaleWorker 使用不同 COM，互不干扰。
/// </summary>
public sealed class BarcodeSerialService : IDisposable
{
    private readonly object _lock = new();
    private readonly StringBuilder _buf = new();
    private SerialPort? _port;

    public event Action<string>? BarcodeScanned;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? ConnectionError;

    public string PortName { get; private set; } = "";
    public int BaudRate { get; private set; } = 9600;
    public bool IsOpen => _port is { IsOpen: true };
    public string LastError { get; private set; } = "";

    public bool Open(string portName, int baudRate = 9600)
    {
        Close();
        PortName = portName?.Trim() ?? "";
        BaudRate = baudRate > 0 ? baudRate : 9600;
        if (string.IsNullOrWhiteSpace(PortName))
        {
            LastError = "未指定扫码串口";
            ConnectionError?.Invoke(LastError);
            return false;
        }

        try
        {
            var port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true,
            };
            port.DataReceived += OnDataReceived;
            port.Open();
            lock (_lock)
            {
                _port = port;
                _buf.Clear();
            }
            LastError = "";
            Connected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ConnectionError?.Invoke(ex.Message);
            return false;
        }
    }

    public void Close()
    {
        SerialPort? port;
        lock (_lock)
        {
            port = _port;
            _port = null;
            _buf.Clear();
        }
        if (port is null) return;
        try
        {
            port.DataReceived -= OnDataReceived;
            if (port.IsOpen) port.Close();
        }
        catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
        Disconnected?.Invoke();
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        string chunk;
        try
        {
            lock (_lock)
            {
                if (_port is null || !_port.IsOpen) return;
                chunk = _port.ReadExisting();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ConnectionError?.Invoke(ex.Message);
            return;
        }

        if (string.IsNullOrEmpty(chunk)) return;

        List<string>? lines = null;
        lock (_lock)
        {
            foreach (var ch in chunk)
            {
                if (ch is '\r' or '\n')
                {
                    var line = _buf.ToString().Trim();
                    _buf.Clear();
                    if (line.Length >= 4)
                    {
                        lines ??= new List<string>();
                        lines.Add(line);
                    }
                }
                else if (!char.IsControl(ch))
                {
                    _buf.Append(ch);
                }
            }
        }

        if (lines is null) return;
        foreach (var line in lines)
            BarcodeScanned?.Invoke(line);
    }

    public void Dispose() => Close();
}
