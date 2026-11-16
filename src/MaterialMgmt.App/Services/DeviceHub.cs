using System.IO.Ports;
using System.Windows;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.App.Services;

/// <summary>主窗口级设备与会话状态（天平 / 扫码），含自动识别与重连。</summary>
public sealed class DeviceHub : IDisposable
{
    private ScaleWorker? _scale;
    private DateTime _lastScanAt = DateTime.MinValue;
    private DateTime _retryAfter = DateTime.MinValue;
    private string _preferredPort = "";
    private bool _autoReconnect = true;
    private bool _userDisconnected;
    private int _discovering; // 0/1
    private bool _hidWired;
    private bool _serialWired;
    private ScannerInputMode _activeMode = ScannerInputMode.Auto;
    private bool _useSerialPath;

    public event Action? ScaleStatusChanged;
    public event Action? ScannerStatusChanged;
    public event Action<string, bool>? BarcodeRouted;

    /// <summary>解析当前应接收扫码的页面处理器（主窗/称量窗）。</summary>
    public Func<IBarcodeHandler?>? ResolveBarcodeHandler { get; set; }

    /// <summary>称量进行中时为 true：扫码只进称量窗，不广播给主窗弹窗。</summary>
    public Func<bool>? IsBarcodeExclusive { get; set; }

    public BarcodeInputService Barcode { get; }
    public BarcodeSerialService SerialBarcode { get; }

    public bool IsScaleConnected => _scale?.IsOpen == true;
    public string LastScanCode { get; private set; } = "";
    public bool IsScannerListening { get; private set; }
    public string ScaleLastError { get; private set; } = "";
    public string ScannerLastError { get; private set; } = "";
    public string ConnectedPort => _scale?.IsOpen == true ? _scale.PortName : _preferredPort;
    public string ScannerConnectedPort => SerialBarcode.IsOpen ? SerialBarcode.PortName : "";
    public bool IsScannerSerialConnected => SerialBarcode.IsOpen;
    /// <summary>当前实际收码通道文案：串口 / HID / 未启用。</summary>
    public string ScannerSourceLabel { get; private set; } = "未启用";
    public bool IsDiscovering => Interlocked.CompareExchange(ref _discovering, 0, 0) == 1;

    /// <summary>是否应将键盘预览键喂给 HID 扫码聚合。</summary>
    public bool ShouldAcceptHidKeys => !_useSerialPath && _hidWired;

    public DeviceHub(BarcodeInputService barcode, BarcodeSerialService serialBarcode)
    {
        Barcode = barcode;
        SerialBarcode = serialBarcode;
        WireHid();
        WireSerial();
    }

    private void WireHid()
    {
        if (_hidWired) return;
        _hidWired = true;
        Barcode.BarcodeScanned += OnHidBarcode;
        Barcode.ActivityDetected += () =>
        {
            if (_useSerialPath) return;
            IsScannerListening = true;
            ScannerStatusChanged?.Invoke();
        };
    }

    private void WireSerial()
    {
        if (_serialWired) return;
        _serialWired = true;
        SerialBarcode.BarcodeScanned += OnSerialBarcode;
        SerialBarcode.Connected += () =>
        {
            ScannerLastError = "";
            UpdateScannerSourceLabel();
            ScannerStatusChanged?.Invoke();
        };
        SerialBarcode.Disconnected += () =>
        {
            UpdateScannerSourceLabel();
            ScannerStatusChanged?.Invoke();
        };
        SerialBarcode.ConnectionError += msg =>
        {
            ScannerLastError = msg;
            UpdateScannerSourceLabel();
            ScannerStatusChanged?.Invoke();
        };
    }

    private void OnHidBarcode(string code)
    {
        if (_useSerialPath) return;
        PublishBarcode(code, simulated: false);
    }

    private void OnSerialBarcode(string code) => PublishBarcode(code, simulated: false);

    private void PublishBarcode(string code, bool simulated)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length < 4) return;
        LastScanCode = normalized;
        _lastScanAt = DateTime.Now;
        IsScannerListening = true;
        ScannerStatusChanged?.Invoke();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            DispatchBarcode(normalized, simulated);
        else if (dispatcher.CheckAccess())
            DispatchBarcode(normalized, simulated);
        else
            dispatcher.Invoke(() => DispatchBarcode(normalized, simulated));
    }

    private void DispatchBarcode(string normalized, bool simulated)
    {
        var exclusive = IsBarcodeExclusive?.Invoke() == true;
        var handler = ResolveBarcodeHandler?.Invoke();
        handler?.OnBarcode(normalized, simulated);
        // 独占称量时不广播，避免主窗筛选/弹窗抢码
        if (!exclusive)
            BarcodeRouted?.Invoke(normalized, simulated);
    }

    public static string NormalizeCode(string code) => (code ?? "").Trim();

    public ScaleWorker? GetScaleWorker() => _scale;

    public void ConfigureAutoReconnect(IConfigService config)
    {
        _autoReconnect = config.AutoConnect;
        _preferredPort = config.SerialPort;
    }

    /// <summary>登录后：电子秤自动发现 + 按配置打开扫码通道。</summary>
    public void BeginAutoConnectAfterLogin(IConfigService config)
    {
        _userDisconnected = false;
        if (!config.AutoConnect)
            config.AutoConnect = true;
        ConfigureAutoReconnect(config);
        BeginDiscoverAndConnect(config);
        ApplyScannerConfig(config);
    }

    /// <summary>按配置应用扫码模式（串口 / HID / 自动）。</summary>
    public void ApplyScannerConfig(IConfigService config)
    {
        _activeMode = config.ScannerMode;
        if (!config.ScannerEnabled)
        {
            SerialBarcode.Close();
            _useSerialPath = false;
            ScannerSourceLabel = "已关闭";
            ScannerLastError = "";
            ScannerStatusChanged?.Invoke();
            return;
        }

        var wantSerial = _activeMode == ScannerInputMode.Serial
            || (_activeMode == ScannerInputMode.Auto && !string.IsNullOrWhiteSpace(config.ScannerPort));

        if (!wantSerial)
        {
            SerialBarcode.Close();
            _useSerialPath = false;
            ScannerSourceLabel = "HID 键盘";
            ScannerLastError = "";
            IsScannerListening = true;
            ScannerStatusChanged?.Invoke();
            return;
        }

        var scannerPort = config.ScannerPort.Trim();
        var scalePort = string.IsNullOrWhiteSpace(ConnectedPort) ? config.SerialPort : ConnectedPort;
        if (!string.IsNullOrWhiteSpace(scannerPort) &&
            !string.IsNullOrWhiteSpace(scalePort) &&
            string.Equals(scannerPort, scalePort, StringComparison.OrdinalIgnoreCase))
        {
            SerialBarcode.Close();
            _useSerialPath = false;
            ScannerLastError = $"扫码口不能与电子秤相同（{scannerPort}）";
            ScannerSourceLabel = "配置冲突";
            ScannerStatusChanged?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(scannerPort))
        {
            SerialBarcode.Close();
            if (_activeMode == ScannerInputMode.Serial)
            {
                _useSerialPath = true;
                ScannerLastError = "未配置扫码串口";
                ScannerSourceLabel = "串口未配置";
            }
            else
            {
                _useSerialPath = false;
                ScannerSourceLabel = "HID 键盘";
                ScannerLastError = "";
                IsScannerListening = true;
            }
            ScannerStatusChanged?.Invoke();
            return;
        }

        _useSerialPath = true;
        if (SerialBarcode.IsOpen &&
            string.Equals(SerialBarcode.PortName, scannerPort, StringComparison.OrdinalIgnoreCase) &&
            SerialBarcode.BaudRate == config.ScannerBaud)
        {
            UpdateScannerSourceLabel();
            ScannerStatusChanged?.Invoke();
            return;
        }

        if (!SerialBarcode.Open(scannerPort, config.ScannerBaud))
        {
            ScannerLastError = SerialBarcode.LastError;
            if (_activeMode == ScannerInputMode.Auto)
            {
                // 串口失败时回退 HID，保证称量可继续
                _useSerialPath = false;
                ScannerSourceLabel = "HID 回退";
            }
            else
            {
                ScannerSourceLabel = "串口失败";
            }
            ScannerStatusChanged?.Invoke();
            return;
        }

        ScannerLastError = "";
        UpdateScannerSourceLabel();
        IsScannerListening = true;
        ScannerStatusChanged?.Invoke();
    }

    private void UpdateScannerSourceLabel()
    {
        if (_useSerialPath && SerialBarcode.IsOpen)
            ScannerSourceLabel = $"串口 {SerialBarcode.PortName}";
        else if (_useSerialPath)
            ScannerSourceLabel = string.IsNullOrEmpty(ScannerLastError) ? "串口" : "串口失败";
        else
            ScannerSourceLabel = "HID 键盘";
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

        // 串口应开却断了 → 尝试重连
        if (config.ScannerEnabled &&
            (_activeMode == ScannerInputMode.Serial ||
             (_activeMode == ScannerInputMode.Auto && !string.IsNullOrWhiteSpace(config.ScannerPort))) &&
            !SerialBarcode.IsOpen &&
            !string.IsNullOrWhiteSpace(config.ScannerPort))
        {
            ApplyScannerConfig(config);
        }

        _autoReconnect = config.AutoConnect;
        if (_userDisconnected || !_autoReconnect || IsScaleConnected || IsDiscovering) return;
        if (DateTime.Now < _retryAfter) return;

        var preferred = string.IsNullOrWhiteSpace(_preferredPort) ? config.SerialPort : _preferredPort;
        var baud = config.SerialBaud;
        var ports = ScaleWorker.ListPorts()
            .Where(p => !IsScannerPort(p, config))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferred) &&
            ports.Any(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase)))
        {
            ConnectScale(preferred, baud, fromUser: false);
            return;
        }

        BeginDiscoverAndConnect(config);
    }

    private static bool IsScannerPort(string port, IConfigService config)
        => !string.IsNullOrWhiteSpace(config.ScannerPort) &&
           string.Equals(port, config.ScannerPort, StringComparison.OrdinalIgnoreCase);

    public void SimulateBarcode(string code)
    {
        PublishBarcode(code, simulated: true);
    }

    /// <summary>硬件自检：返回 (秤口可开, 扫码通道说明)。</summary>
    public (bool ScaleOk, string ScaleMsg, bool ScannerOk, string ScannerMsg) SelfCheck(IConfigService config)
    {
        var scalePort = config.SerialPort;
        var scaleOk = false;
        var scaleMsg = "未配置电子秤端口";
        if (!string.IsNullOrWhiteSpace(scalePort))
        {
            try
            {
                using var sp = new SerialPort(scalePort, config.SerialBaud);
                sp.Open();
                sp.Close();
                scaleOk = true;
                scaleMsg = $"{scalePort} 可打开";
            }
            catch (Exception ex)
            {
                scaleMsg = $"{scalePort}：{ex.Message}";
            }
        }

        ApplyScannerConfig(config);
        bool scannerOk;
        string scannerMsg;
        if (!config.ScannerEnabled)
        {
            scannerOk = true;
            scannerMsg = "扫码已关闭";
        }
        else if (_useSerialPath)
        {
            scannerOk = SerialBarcode.IsOpen;
            scannerMsg = scannerOk
                ? $"串口 {SerialBarcode.PortName} @ {SerialBarcode.BaudRate} 已连接，请扫一张测试"
                : $"串口失败：{ScannerLastError}";
        }
        else
        {
            scannerOk = true;
            scannerMsg = "HID 模式：请聚焦窗口后扫码测试";
        }

        return (scaleOk, scaleMsg, scannerOk, scannerMsg);
    }

    private void BeginDiscoverAndConnect(IConfigService config)
    {
        if (IsScaleConnected) return;
        if (Interlocked.CompareExchange(ref _discovering, 1, 0) != 0) return;

        ScaleLastError = "正在识别电子秤…";
        ScaleStatusChanged?.Invoke();

        var prefer = new[] { config.SerialPort, _preferredPort };
        var baud = config.SerialBaud;
        var excludeScanner = config.ScannerPort;

        Task.Run(() =>
        {
            try
            {
                string? found = null;
                var exclude = string.IsNullOrWhiteSpace(excludeScanner)
                    ? Array.Empty<string>()
                    : new[] { excludeScanner };
                found = ScaleWorker.FindScalePort(baud, prefer, exclude);

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

    public void Dispose()
    {
        DisconnectScale();
        SerialBarcode.Close();
    }
}

public static class RoleLabels
{
    public static string ToDisplay(string role) => role switch
    {
        Roles.SystemAdmin or Roles.LegacyAdmin => "系统管理员",
        Roles.Business or Roles.LegacyOperator => "业务人员",
        Roles.LegacyRoot => "已废弃(root)",
        _ => role
    };
}
