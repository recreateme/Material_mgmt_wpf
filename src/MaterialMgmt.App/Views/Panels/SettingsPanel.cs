using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.App.Views.Panels;

public sealed class SettingsPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IConfigService _config;
    private readonly IAuthService _auth;
    private readonly DeviceHub _devices;
    private readonly SoundService _sound;
    private readonly CheckBox _soundOn = new() { Content = "称量时播放提示音", Margin = new Thickness(0, 8, 0, 8) };
    private readonly ComboBox _ports = new() { Width = 180, Height = 30 };
    private readonly CheckBox _auto = new() { Content = "登录后自动识别并连接电子秤", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _scaleStatus = new() { Margin = new Thickness(0, 8, 0, 0) };

    private readonly ComboBox _scannerPorts = new() { Width = 180, Height = 30 };
    private readonly ComboBox _scannerMode = new() { Width = 160, Height = 30 };
    private readonly ComboBox _scannerBaud = new() { Width = 100, Height = 30 };
    private readonly CheckBox _scannerEnabled = new() { Content = "启用扫码枪", Margin = new Thickness(0, 8, 0, 8), IsChecked = true };
    private readonly TextBlock _scanStatus = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _scanHint = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _simCode = new() { Width = 220, Height = 30, Text = "MAT-000001" };

    public SettingsPanel(IConfigService config, IAuthService auth, DeviceHub devices, SoundService sound)
    {
        _sound = sound;
        _config = config;
        _auth = auth;
        _devices = devices;
        Build();
        _devices.ScaleStatusChanged += () => Dispatcher.Invoke(UpdateScaleStatus);
        _devices.ScannerStatusChanged += () => Dispatcher.Invoke(RefreshScannerUi);
    }

    public void OnShow()
    {
        ReloadPorts();
        _auto.IsChecked = _config.AutoConnect;
        _soundOn.IsChecked = _config.SoundEnabled;
        _scannerEnabled.IsChecked = _config.ScannerEnabled;
        _scannerMode.SelectedValue = _config.ScannerMode;
        _scannerBaud.SelectedItem = _config.ScannerBaud.ToString();
        UpdateScaleStatus();
        RefreshScannerUi();
    }

    public void OnBarcode(string code, bool simulated = false)
    {
        _scanHint.Text = (simulated ? "[模拟] " : "") + $"最近扫码：{code}  ({DateTime.Now:HH:mm:ss})";
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "系统设置", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });

        root.Children.Add(new TextBlock { Text = "电子天平（串口）", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(_ports);
        var refresh = UiTheme.GhostButton("刷新端口");
        refresh.Click += (_, _) => ReloadPorts();
        row.Children.Add(refresh);
        var test = UiTheme.GhostButton("测试连接");
        test.Click += (_, _) => TestConnect();
        row.Children.Add(test);
        var save = UiTheme.PrimaryButton("保存并连接");
        save.Click += (_, _) => SaveAndConnect();
        row.Children.Add(save);
        root.Children.Add(row);
        root.Children.Add(_auto);
        root.Children.Add(_scaleStatus);

        root.Children.Add(new TextBlock
        {
            Text = "提示音", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 24, 0, 0)
        });
        _soundOn.Click += (_, _) => _config.SoundEnabled = _soundOn.IsChecked == true;
        root.Children.Add(_soundOn);
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal };
        soundRow.Children.Add(new TextBlock
        {
            Text = "试听：", VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Brush(UiTheme.Muted)
        });
        foreach (var (label, cue) in new[]
        {
            ("合格", SoundCue.StableOk), ("超范围", SoundCue.OutOfRange),
            ("扫错码", SoundCue.WrongScan), ("读数中断", SoundCue.ReadingLost)
        })
        {
            var b = UiTheme.GhostButton(label);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += (_, _) => _sound.Preview(cue);
            soundRow.Children.Add(b);
        }
        root.Children.Add(soundRow);

        root.Children.Add(new TextBlock
        {
            Text = "扫码枪", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 24, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "推荐：扫码枪设为 USB COM，与电子秤使用不同 COM。标签内容须与原料编码一致（如 MAT-000001）。",
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(_scannerEnabled);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        modeRow.Children.Add(new TextBlock { Text = "模式", VerticalAlignment = VerticalAlignment.Center, Width = 56 });
        _scannerMode.ItemsSource = new[]
        {
            new ModeItem(ScannerInputMode.Auto, "自动（有 COM 用串口）"),
            new ModeItem(ScannerInputMode.Serial, "仅串口"),
            new ModeItem(ScannerInputMode.Hid, "仅 HID 键盘")
        };
        _scannerMode.DisplayMemberPath = nameof(ModeItem.Label);
        _scannerMode.SelectedValuePath = nameof(ModeItem.Mode);
        modeRow.Children.Add(_scannerMode);
        root.Children.Add(modeRow);

        var scanRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        scanRow.Children.Add(_scannerPorts);
        _scannerBaud.ItemsSource = new[] { "9600", "115200", "57600", "38400", "19200" };
        scanRow.Children.Add(_scannerBaud);
        var refreshScan = UiTheme.GhostButton("刷新");
        refreshScan.Click += (_, _) => ReloadPorts();
        scanRow.Children.Add(refreshScan);
        var saveScan = UiTheme.PrimaryButton("保存扫码设置");
        saveScan.Click += (_, _) => SaveScanner();
        scanRow.Children.Add(saveScan);
        root.Children.Add(scanRow);
        root.Children.Add(_scanStatus);
        root.Children.Add(_scanHint);

        var simRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        // 模拟扫码仅用于设备联调，限系统管理员；称量流程本身也会拒绝模拟扫码
        if (_auth.CurrentUser?.IsSystemAdmin() == true)
        {
            simRow.Children.Add(_simCode);
            var sim = UiTheme.GhostButton("模拟扫码（联调）");
            sim.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_simCode.Text))
                    _devices.SimulateBarcode(_simCode.Text.Trim());
            };
            simRow.Children.Add(sim);
        }
        var selfCheck = UiTheme.GhostButton("硬件自检");
        selfCheck.Click += (_, _) => RunSelfCheck();
        simRow.Children.Add(selfCheck);
        root.Children.Add(simRow);

        root.Children.Add(new TextBlock
        {
            Text = "账户", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 24, 0, 8)
        });
        var pw = UiTheme.PrimaryButton("修改密码");
        pw.Click += (_, _) =>
        {
            var u = _auth.CurrentUser;
            if (u is null) return;
            new Views.Dialogs.ChangePasswordDialog(_auth, u.Id) { Owner = Window.GetWindow(this) }.ShowDialog();
        };
        root.Children.Add(pw);

        root.Children.Add(new TextBlock
        {
            Text = "关于", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 24, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"{AppInfo.WindowTitle}\n构建时间：{AppInfo.BuildDateDisplay}\n运行时：.NET {Environment.Version}",
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        Content = new ScrollViewer { Content = root };
    }

    private void ReloadPorts()
    {
        var ports = ScaleWorker.ListPorts().ToList();
        _ports.ItemsSource = ports;
        var cur = _config.SerialPort;
        if (!string.IsNullOrEmpty(cur) && ports.Contains(cur))
            _ports.SelectedItem = cur;
        else if (ports.Count > 0)
            _ports.SelectedIndex = 0;

        var scanPorts = new List<string> { "" };
        scanPorts.AddRange(ports);
        _scannerPorts.ItemsSource = scanPorts;
        var scanCur = _config.ScannerPort;
        if (!string.IsNullOrEmpty(scanCur) && ports.Contains(scanCur))
            _scannerPorts.SelectedItem = scanCur;
        else
            _scannerPorts.SelectedIndex = 0;
    }

    private void TestConnect()
    {
        var port = _ports.SelectedItem as string;
        if (string.IsNullOrEmpty(port))
        {
            MessageBox.Show("请选择 COM 口");
            return;
        }
        try
        {
            using var sp = new System.IO.Ports.SerialPort(port, _config.SerialBaud);
            sp.Open();
            sp.Close();
            MessageBox.Show($"端口 {port} 可打开", "成功");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"测试失败：{ex.Message}", "失败");
        }
    }

    private void SaveAndConnect()
    {
        var port = _ports.SelectedItem as string ?? "";
        var scannerPort = _scannerPorts.SelectedItem as string ?? "";
        if (!string.IsNullOrEmpty(port) &&
            !string.IsNullOrEmpty(scannerPort) &&
            string.Equals(port, scannerPort, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("电子秤与扫码枪不能使用同一 COM 口。", "端口冲突",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _config.SerialPort = port;
        _config.AutoConnect = _auto.IsChecked == true;
        _devices.ConfigureAutoReconnect(_config);
        if (!string.IsNullOrEmpty(port))
            _devices.ConnectScale(port, _config.SerialBaud);
        _devices.ApplyScannerConfig(_config);
        UpdateScaleStatus();
        RefreshScannerUi();
        MessageBox.Show("已保存", "提示");
    }

    private void SaveScanner()
    {
        var scannerPort = _scannerPorts.SelectedItem as string ?? "";
        var scalePort = _ports.SelectedItem as string ?? _config.SerialPort;
        if (!string.IsNullOrEmpty(scannerPort) &&
            !string.IsNullOrEmpty(scalePort) &&
            string.Equals(scannerPort, scalePort, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("扫码枪 COM 不能与电子秤相同。", "端口冲突",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ScannerEnabled = _scannerEnabled.IsChecked == true;
        if (_scannerMode.SelectedValue is ScannerInputMode mode)
            _config.ScannerMode = mode;
        _config.ScannerPort = scannerPort;
        if (_scannerBaud.SelectedItem is string baudText && int.TryParse(baudText, out var baud))
            _config.ScannerBaud = baud;

        _devices.ApplyScannerConfig(_config);
        RefreshScannerUi();
        MessageBox.Show(
            _devices.IsScannerSerialConnected
                ? $"扫码串口已连接：{_devices.ScannerConnectedPort}"
                : $"扫码通道：{_devices.ScannerSourceLabel}" +
                  (string.IsNullOrEmpty(_devices.ScannerLastError) ? "" : $"\n{_devices.ScannerLastError}"),
            "扫码设置");
    }

    private void RunSelfCheck()
    {
        // 先落盘当前选择再测
        _config.ScannerEnabled = _scannerEnabled.IsChecked == true;
        if (_scannerMode.SelectedValue is ScannerInputMode mode)
            _config.ScannerMode = mode;
        _config.ScannerPort = _scannerPorts.SelectedItem as string ?? "";
        if (_scannerBaud.SelectedItem is string baudText && int.TryParse(baudText, out var baud))
            _config.ScannerBaud = baud;

        var (scaleOk, scaleMsg, scannerOk, scannerMsg) = _devices.SelfCheck(_config);
        RefreshScannerUi();
        MessageBox.Show(
            $"电子秤：{(scaleOk ? "✓" : "✗")} {scaleMsg}\n扫码枪：{(scannerOk ? "✓" : "✗")} {scannerMsg}",
            "硬件自检",
            MessageBoxButton.OK,
            scaleOk && scannerOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void UpdateScaleStatus()
    {
        if (_devices.IsScaleConnected)
        {
            _scaleStatus.Text = $"状态：已连接（{_devices.ConnectedPort}）";
            _scaleStatus.Foreground = new SolidColorBrush(Colors.SeaGreen);
            return;
        }
        if (_devices.IsDiscovering || _devices.ScaleLastError.Contains("识别", StringComparison.Ordinal))
        {
            _scaleStatus.Text = $"状态：{_devices.ScaleLastError}";
            _scaleStatus.Foreground = new SolidColorBrush(Colors.DarkOrange);
            return;
        }
        _scaleStatus.Text = string.IsNullOrEmpty(_devices.ScaleLastError)
            ? "状态：未连接"
            : $"状态：未连接 — {_devices.ScaleLastError}";
        _scaleStatus.Foreground = new SolidColorBrush(Colors.IndianRed);
    }

    private void RefreshScannerUi()
    {
        _scanStatus.Text = $"通道：{_devices.ScannerSourceLabel}" +
                           (string.IsNullOrEmpty(_devices.ScannerLastError) ? "" : $" — {_devices.ScannerLastError}");
        _scanStatus.Foreground = new SolidColorBrush(
            _devices.IsScannerSerialConnected || _devices.ScannerSourceLabel.Contains("HID", StringComparison.Ordinal)
                ? Colors.SeaGreen
                : Colors.IndianRed);

        if (string.IsNullOrEmpty(_devices.LastScanCode))
            _scanHint.Text = _devices.IsScannerListening ? "扫码枪监听中…请扫一张测试" : "等待扫码…";
        else
            _scanHint.Text = $"最近扫码：{_devices.LastScanCode}";
    }

    private sealed record ModeItem(ScannerInputMode Mode, string Label);
}
