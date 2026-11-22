using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace MaterialMgmt.Infrastructure.Hardware;

/// <summary>电子天平串口轮询（对齐 Python ScaleWorker Modbus-RTU）。</summary>
public sealed class ScaleWorker : IDisposable
{
    /// <summary>功能码 03，从寄存器 0 读 4 个寄存器。末两字节是 CRC。现场协议不同时改这里。</summary>
    private static readonly byte[] ReadCmd = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x04, 0x44, 0x09 };
    /// <summary>功能码 06，把寄存器 4 写成 1，用于置零。</summary>
    private static readonly byte[] ZeroCmd = { 0x01, 0x06, 0x00, 0x04, 0x00, 0x01, 0x09, 0xCB };

    private readonly ILogger<ScaleWorker>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private SerialPort? _port;
    private volatile bool _sendZero;

    public string PortName { get; }
    public int BaudRate { get; }
    public bool IsOpen => _port is { IsOpen: true };

    public event Action<double, bool, bool>? WeightUpdated; // weight, stable, overload
    public event Action<string>? ConnectionError;
    public event Action? Connected;
    public event Action? Disconnected;

    /// <summary>原始帧日志（调试天平协议用，默认不设置）：参数为“方向 + 十六进制帧”。</summary>
    public Action<string>? FrameLog { get; set; }

    public ScaleWorker(string portName, int baudRate = 9600, ILogger<ScaleWorker>? logger = null)
    {
        PortName = portName;
        BaudRate = baudRate;
        _logger = logger;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>
    /// 停止轮询。先关闭串口让阻塞中的读写立即失败，再短暂等待循环退出，
    /// 调用线程（通常是界面线程）不再被 600ms 读超时拖住；返回时串口已释放，可立即重连同一端口。
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        ClosePort();
        try { _loop?.Wait(500); } catch { /* ignore */ }
    }

    public void SendZero() => _sendZero = true;

    private async Task RunAsync(CancellationToken ct)
    {
        SerialPort port;
        try
        {
            port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 600,
                WriteTimeout = 600
            };
            port.Open();
            _port = port;
            if (ct.IsCancellationRequested)
            {
                ClosePort();
                return;
            }
            Connected?.Invoke();
            _logger?.LogInformation("天平已连接：{Port} {Baud}", PortName, BaudRate);
        }
        catch (Exception ex)
        {
            ConnectionError?.Invoke(ex.Message);
            return;
        }

        var failStreak = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_sendZero)
                    {
                        port.Write(ZeroCmd, 0, ZeroCmd.Length);
                        LogFrame("TX", ZeroCmd);
                        LogFrame("RX", ReadExact(port, 8));
                        _sendZero = false;
                    }

                    port.Write(ReadCmd, 0, ReadCmd.Length);
                    LogFrame("TX", ReadCmd);
                    var data = ReadExact(port, 13);
                    LogFrame("RX", data);
                    var parsed = Parse(data);
                    if (parsed is { } p)
                    {
                        failStreak = 0;
                        WeightUpdated?.Invoke(p.Weight, p.Stable, p.Overload);
                    }
                    else
                    {
                        failStreak++;
                        if (failStreak >= 15)
                            throw new IOException("连续无有效响应，天平可能已断开");
                    }
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException
                                               or InvalidOperationException or ObjectDisposedException)
                {
                    if (ct.IsCancellationRequested) break;
                    failStreak++;
                    _logger?.LogWarning(ex, "天平读取异常");
                    if (failStreak >= 15)
                    {
                        ConnectionError?.Invoke(ex.Message);
                        break;
                    }
                }

                await Task.Delay(200, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ClosePort();
            Disconnected?.Invoke();
        }
    }

    private static byte[] ReadExact(SerialPort port, int count)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = port.Read(buf, read, count - read);
            if (n <= 0) throw new TimeoutException("串口读超时");
            read += n;
        }
        return buf;
    }

    private void LogFrame(string dir, byte[] frame)
    {
        var log = FrameLog;
        if (log is null) return;
        try { log($"{PortName} {dir} {Convert.ToHexString(frame)}"); } catch { /* 日志失败不影响读数 */ }
    }

    /// <summary>可由 Stop（界面线程）与读循环（后台线程）同时调用，只有一方真正关闭。</summary>
    private void ClosePort()
    {
        var port = Interlocked.Exchange(ref _port, null);
        if (port is null) return;
        try
        {
            if (port.IsOpen) port.Close();
        }
        catch { /* ignore */ }
        port.Dispose();
    }

    internal static (double Weight, bool Stable, bool Overload)? Parse(byte[] data)
    {
        if (data.Length < 13) return null;
        if (data[0] != 0x01 || data[1] != 0x03 || data[2] != 0x08) return null;
        var crcRecv = data[11] | (data[12] << 8);
        if (Crc16(data.AsSpan(0, 11)) != crcRecv) return null;

        // 大端有符号 32 位
        var wRaw = (data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6];
        var weightInt = unchecked((int)(uint)wRaw);
        var dot = data[8];
        var st = data[10];
        var weight = weightInt / Math.Pow(10, dot);
        return (weight, (st & 0x02) != 0, (st & 0x04) != 0);
    }

    /// <summary>探测端口是否为可响应的天平（Modbus 读重量）。</summary>
    public static bool TryProbe(string portName, int baudRate = 9600, int timeoutMs = 900)
    {
        SerialPort? sp = null;
        try
        {
            sp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = Math.Max(200, timeoutMs),
                WriteTimeout = 600
            };
            sp.Open();
            sp.DiscardInBuffer();
            sp.DiscardOutBuffer();
            sp.Write(ReadCmd, 0, ReadCmd.Length);

            var data = new byte[13];
            var read = 0;
            var deadline = Environment.TickCount64 + timeoutMs;
            while (read < 13 && Environment.TickCount64 < deadline)
            {
                var avail = sp.BytesToRead;
                if (avail > 0)
                    read += sp.Read(data, read, Math.Min(avail, 13 - read));
                else
                    Thread.Sleep(20);
            }
            if (read < 13) return false;
            return Parse(data) is not null;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (sp is { IsOpen: true }) sp.Close(); } catch { /* ignore */ }
            sp?.Dispose();
        }
    }

    /// <summary>按优先端口顺序扫描，返回第一个可识别为天平的 COM 口。</summary>
    public static string? FindScalePort(
        int baudRate = 9600,
        IEnumerable<string>? preferFirst = null,
        IEnumerable<string>? excludePorts = null)
    {
        var exclude = new HashSet<string>(
            (excludePorts ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var available = ListPorts()
            .Where(p => !exclude.Contains(p))
            .ToList();
        if (available.Count == 0) return null;

        var ordered = new List<string>();
        if (preferFirst is not null)
        {
            foreach (var p in preferFirst)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var hit = available.FirstOrDefault(x => string.Equals(x, p.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit is not null && !ordered.Contains(hit, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(hit);
            }
        }
        foreach (var p in available)
        {
            if (!ordered.Contains(p, StringComparer.OrdinalIgnoreCase))
                ordered.Add(p);
        }

        foreach (var port in ordered)
        {
            if (TryProbe(port, baudRate))
                return port;
        }
        return null;
    }

    private static int Crc16(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
        }
        return crc;
    }

    public void Dispose() => Stop();

    public static IReadOnlyList<string> ListPorts() => SerialPort.GetPortNames().OrderBy(x => x).ToList();
}
