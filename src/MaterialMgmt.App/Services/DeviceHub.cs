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
    private readonly BarcodeRouter _router = new();
    private DateTime _retryAfter = DateTime.MinValue;
    private string _preferredPort = "";
    private bool _autoReconnect = true;
    private bool _userDisconnected;
    private int _discovering; // 0/1
    private bool _hidWired;
    private bool _serialWired;
    private ScannerInputMode _activeMode = ScannerInputMode.Auto;
    private bool _rawFrameLog;

    private bool UseSerialPath
    {
        get => _router.SerialPathActive;
        set => _router.SerialPathActive = value;
    }

    public event Action? ScaleStatusChanged;
    public event Action? ScannerStatusChanged;

    public event Action<string, bool>? BarcodeRouted
    {
        add => _router.Broadcast += value;
        remove => _router.Broadcast -= value;
    }

    /// <summary>
    /// 稳定的称重事件（weight, stable, overload）：不随 ScaleWorker 重建而失效，
    /// 在 ScaleWorker 线程触发，订阅方自行切换到 UI 线程。
    /// </summary>
    public event Action<double, bool, bool>? WeightUpdated;

    /// <summary>解析当前应接收扫码的页面处理器（主窗/称量窗）。</summary>
    public Func<IBarcodeHandler?>? ResolveBarcodeHandler { get; set; }

    /// <summary>称量进行中时为 true：扫码只进称量窗，不广播给主窗弹窗。</summary>
    public Func<bool>? IsBarcodeExclusive
    {
        get => _router.IsExclusive;
        set => _router.IsExclusive = value;
    }

    public BarcodeInputService Barcode { get; }
    public BarcodeSerialService SerialBarcode { get; }

    public bool IsScaleConnected => _scale?.IsOpen == true;
    public string LastScanCode => _router.LastCode;
    /// <summary>最近一次扫码的来源（<see cref="ScanSources"/>）；分发给页面前已更新，页面在 OnBarcode 内读取即为本次来源。</summary>
    public string LastScanSource => _router.LastSource;
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
    public bool ShouldAcceptHidKeys => !UseSerialPath && _hidWired;

    public DeviceHub(BarcodeInputService barcode, BarcodeSerialService serialBarcode)
    {
        Barcode = barcode;
        SerialBarcode = serialBarcode;
        _router.ResolveHandler = () => ResolveBarcodeHandler?.Invoke() is { } h ? h.OnBarcode : null;
        _router.Marshal = dispatch =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) dispatch();
            else dispatcher.Invoke(dispatch);
        };
        _router.Accepted += () =>
        {
            IsScannerListening = true;
            ScannerStatusChanged?.Invoke();
        };
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
            if (UseSerialPath) return;
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

    private void OnHidBarcode(string code) => _router.Publish(code, ScanSources.Hid);

    private void OnSerialBarcode(string code) => _router.Publish(code, ScanSources.Serial);

    public static string NormalizeCode(string code) => BarcodeRouter.Normalize(code);

    public ScaleWorker? GetScaleWorker() => _scale;

    public void ConfigureAutoReconnect(IConfigService config)
    {
        _autoReconnect = config.AutoConnect;
        _preferredPort = config.SerialPort;
        _rawFrameLog = IsRawFrameLogOn(config);
    }

    /// <summary>[serial] raw_frame_log = true 时把天平收发的原始帧写入日志（排查协议问题用，日志量较大）。</summary>
    private static bool IsRawFrameLogOn(IConfigService config)
        => bool.TryParse(config.GetValue("serial", "raw_frame_log", "false"), out var v) && v;

    private static void WriteFrameLog(string line) => Serilog.Log.Information("天平原始帧 {Frame}", line);

    /// <summary>登录后：电子秤自动发现 + 按配置打开扫码通道。</summary>
    public void BeginAutoConnectAfterLogin(IConfigService config)
    {
        _userDisconnected = false;
        // 尊重“登录后自动连接”配置：关闭时不探测串口（探测会向所有 COM 口写入 Modbus 帧）
        ConfigureAutoReconnect(config);
        if (config.AutoConnect)
        {
            if (!string.IsNullOrWhiteSpace(config.SerialPort) &&
                ScaleWorker.ListPorts().Any(p => string.Equals(p, config.SerialPort, StringComparison.OrdinalIgnoreCase)))
                ConnectScale(config.SerialPort, config.SerialBaud, fromUser: false);
            else
                BeginDiscoverAndConnect(config);
        }
        ApplyScannerConfig(config);
    }

    /// <summary>按配置应用扫码模式（串口 / HID / 自动）。</summary>
    public void ApplyScannerConfig(IConfigService config)
    {
        _activeMode = config.ScannerMode;
        if (!config.ScannerEnabled)
        {
            SerialBarcode.Close();
            UseSerialPath = false;
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
            UseSerialPath = false;
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
            UseSerialPath = false;
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
                UseSerialPath = true;
                ScannerLastError = "未配置扫码串口";
                ScannerSourceLabel = "串口未配置";
            }
            else
            {
                UseSerialPath = false;
                ScannerSourceLabel = "HID 键盘";
                ScannerLastError = "";
                IsScannerListening = true;
            }
            ScannerStatusChanged?.Invoke();
            return;
        }

        UseSerialPath = true;
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
                UseSerialPath = false;
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
        if (UseSerialPath && SerialBarcode.IsOpen)
            ScannerSourceLabel = $"串口 {SerialBarcode.PortName}";
        else if (UseSerialPath)
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
            var worker = new ScaleWorker(port, baud);
            if (_rawFrameLog) worker.FrameLog = WriteFrameLog;
            _scale = worker;
            // 仅转发当前 worker 的读数，避免已被替换的旧 worker 残留事件串入
            worker.WeightUpdated += (w, stable, overload) =>
            {
                if (ReferenceEquals(_scale, worker))
                    WeightUpdated?.Invoke(w, stable, overload);
            };
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

    /// <summary>向天平发送清零指令。未连接时返回 false。</summary>
    public bool SendZero()
    {
        var worker = _scale;
        if (worker is not { IsOpen: true }) return false;
        worker.SendZero();
        return true;
    }

    /// <summary>用户点击“重连”：立即重试，不等退避计时；读数已中断但串口尚未判定断开时也会重建连接。</summary>
    public void ReconnectScale(IConfigService config)
    {
        _userDisconnected = false;
        _retryAfter = DateTime.MinValue;
        if (IsDiscovering) return;

        DisconnectScale(keepPreference: true);
        var preferred = string.IsNullOrWhiteSpace(_preferredPort) ? config.SerialPort : _preferredPort;
        if (!string.IsNullOrWhiteSpace(preferred) &&
            ScaleWorker.ListPorts().Any(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase)))
            ConnectScale(preferred, config.SerialBaud, fromUser: false);
        else
            BeginDiscoverAndConnect(config);
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
        if (IsScannerListening && DateTime.Now - _router.LastAt > TimeSpan.FromMinutes(10))
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
        _rawFrameLog = IsRawFrameLogOn(config);
        if (_userDisconnected || !_autoReconnect || IsScaleConnected || IsDiscovering) return;
        if (DateTime.Now < _retryAfter) return;

        var preferred = string.IsNullOrWhiteSpace(_preferredPort) ? config.SerialPort : _preferredPort;
        var baud = config.SerialBaud;
        var ports = ScaleWorker.ListPorts()
            .Where(p => !IsScannerPort(p, config))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            // 已配置秤口：只等待该口出现再连接，不对其他 COM 口发送探测帧
            if (ports.Any(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase)))
                ConnectScale(preferred, baud, fromUser: false);
            else
                ScheduleRetry();
            return;
        }

        // 未配置秤口：才做全口探测，且退避到 30 秒一次
        BeginDiscoverAndConnect(config);
    }

    private static bool IsScannerPort(string port, IConfigService config)
        => !string.IsNullOrWhiteSpace(config.ScannerPort) &&
           string.Equals(port, config.ScannerPort, StringComparison.OrdinalIgnoreCase);

    public void SimulateBarcode(string code)
    {
        _router.Publish(code, ScanSources.Simulated);
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
        else if (UseSerialPath)
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
                        ScheduleRetry(30);
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

    private void ScheduleRetry(int seconds = 5) => _retryAfter = DateTime.Now.AddSeconds(seconds);

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
