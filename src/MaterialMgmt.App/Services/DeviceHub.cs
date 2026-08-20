using System.Windows;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.App.Services;

/// <summary>主窗口级设备与会话状态（天平 / 扫码），含自动识别与重连。</summary>
public sealed class DeviceHub
{
    private ScaleWorker? _scale;
    private DateTime _lastScanAt = DateTime.MinValue;
    private DateTime _retryAfter = DateTime.MinValue;
    private string _preferredPort = "";
    private bool _autoReconnect = true;
    private bool _userDisconnected;
    private int _discovering; // 0/1

    public event Action? ScaleStatusChanged;
    public event Action? ScannerStatusChanged;
    public event Action<string, bool>? BarcodeRouted;

    public BarcodeInputService Barcode { get; }
    public bool IsScaleConnected => _scale?.IsOpen == true;
    public string LastScanCode { get; private set; } = "";
    public bool IsScannerListening { get; private set; }
    public string ScaleLastError { get; private set; } = "";
    public string ConnectedPort => _scale?.IsOpen == true ? _scale.PortName : _preferredPort;
    public bool IsDiscovering => Interlocked.CompareExchange(ref _discovering, 0, 0) == 1;

    public DeviceHub(BarcodeInputService barcode)
    {
        Barcode = barcode;
        barcode.BarcodeScanned += code =>
        {
            LastScanCode = code;
            _lastScanAt = DateTime.Now;
            IsScannerListening = true;
            ScannerStatusChanged?.Invoke();
            Application.Current?.Dispatcher.Invoke(() => BarcodeRouted?.Invoke(code, false));
        };
        barcode.ActivityDetected += () =>
        {
            IsScannerListening = true;
            ScannerStatusChanged?.Invoke();
        };
    }

    public ScaleWorker? GetScaleWorker() => _scale;

    public void ConfigureAutoReconnect(IConfigService config)
    {
        _autoReconnect = config.AutoConnect;
        _preferredPort = config.SerialPort;
    }

    /// <summary>登录后主动识别并连接电子秤（后台探测，不阻塞 UI）。</summary>
    public void BeginAutoConnectAfterLogin(IConfigService config)
    {
        _userDisconnected = false;
        if (!config.AutoConnect)
            config.AutoConnect = true;
        ConfigureAutoReconnect(config);
        BeginDiscoverAndConnect(config);
    }

    public bool ConnectScale(string port, int baud, bool fromUser = true)
    {
        if (fromUser) _userDisconnected = false;
        DisconnectScale(keepPreference: true);
        _preferredPort = port;
        try
        {
            _scale = new ScaleWorker(port, baud);
            _scale.Connected += () =>
            {
                ScaleLastError = "";
                _retryAfter = DateTime.MinValue;
                ScaleStatusChanged?.Invoke();
            };
            _scale.Disconnected += () =>
            {
                ScaleStatusChanged?.Invoke();
                ScheduleRetry();
            };
            _scale.ConnectionError += msg =>
            {
                ScaleLastError = msg;
                ScaleStatusChanged?.Invoke();
                ScheduleRetry();
            };
            _scale.Start();
            return true;
        }
        catch (Exception ex)
        {
            ScaleLastError = ex.Message;
            ScheduleRetry();
            ScaleStatusChanged?.Invoke();
            return false;
        }
    }

    public void DisconnectScale(bool keepPreference = false)
    {
        if (!keepPreference) _userDisconnected = true;
        _scale?.Dispose();
        _scale = null;
        ScaleStatusChanged?.Invoke();
    }

    /// <summary>由主窗口定时器调用：空闲扫码检测 + 天平自动重连/再识别。</summary>
    public void Tick(IConfigService config)
    {
        if (IsScannerListening && DateTime.Now - _lastScanAt > TimeSpan.FromMinutes(10))
        {
            IsScannerListening = false;
            ScannerStatusChanged?.Invoke();
        }

        _autoReconnect = config.AutoConnect;
        if (_userDisconnected || !_autoReconnect || IsScaleConnected || IsDiscovering) return;
        if (DateTime.Now < _retryAfter) return;

        var preferred = string.IsNullOrWhiteSpace(_preferredPort) ? config.SerialPort : _preferredPort;
        var baud = config.SerialBaud;
        var ports = ScaleWorker.ListPorts();

        if (!string.IsNullOrWhiteSpace(preferred) &&
            ports.Any(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase)))
        {
            ConnectScale(preferred, baud, fromUser: false);
            return;
        }

        BeginDiscoverAndConnect(config);
    }

    public void SimulateBarcode(string code)
    {
        LastScanCode = code;
        _lastScanAt = DateTime.Now;
        IsScannerListening = true;
        ScannerStatusChanged?.Invoke();
        Barcode.Simulate(code);
        BarcodeRouted?.Invoke(code, true);
    }

    private void BeginDiscoverAndConnect(IConfigService config)
    {
        if (IsScaleConnected) return;
        if (Interlocked.CompareExchange(ref _discovering, 1, 0) != 0) return;

        ScaleLastError = "正在识别电子秤…";
        ScaleStatusChanged?.Invoke();

        var prefer = new[] { config.SerialPort, _preferredPort };
        var baud = config.SerialBaud;

        Task.Run(() =>
        {
            try
            {
                var found = ScaleWorker.FindScalePort(baud, prefer);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_userDisconnected) return;
                    if (found is null)
                    {
                        ScaleLastError = ScaleWorker.ListPorts().Count == 0
                            ? "未发现串口设备，请检查电子秤连接"
                            : "未识别到电子秤，将自动重试";
                        ScheduleRetry();
                        ScaleStatusChanged?.Invoke();
                        return;
                    }

                    if (!string.Equals(config.SerialPort, found, StringComparison.OrdinalIgnoreCase))
                        config.SerialPort = found;
                    _preferredPort = found;
                    ConnectScale(found, baud, fromUser: false);
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ScaleLastError = ex.Message;
                    ScheduleRetry();
                    ScaleStatusChanged?.Invoke();
                });
            }
            finally
            {
                Interlocked.Exchange(ref _discovering, 0);
            }
        });
    }

    private void ScheduleRetry() => _retryAfter = DateTime.Now.AddSeconds(5);
}

public static class RoleLabels
{
    public static string ToDisplay(string role) => role switch
    {
        Roles.Root => "开发人员",
        Roles.Admin => "管理人员",
        Roles.Operator => "实验人员",
        _ => role
    };
}
