using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.App.Views.Panels;

public sealed class SettingsPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IConfigService _config;
    private readonly IAuthService _auth;
    private readonly DeviceHub _devices;
    private readonly ComboBox _ports = new() { Width = 180, Height = 30 };
    private readonly CheckBox _auto = new() { Content = "登录后自动识别并连接电子秤", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _scaleStatus = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _scanHint = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _simCode = new() { Width = 220, Height = 30, Text = "MAT-000001" };

    public SettingsPanel(IConfigService config, IAuthService auth, DeviceHub devices)
    {
        _config = config;
        _auth = auth;
        _devices = devices;
        Build();
        _devices.ScaleStatusChanged += () => Dispatcher.Invoke(UpdateScaleStatus);
        _devices.ScannerStatusChanged += () => Dispatcher.Invoke(RefreshScannerHint);
    }

    public void OnShow()
    {
        ReloadPorts();
        _auto.IsChecked = _config.AutoConnect;
        UpdateScaleStatus();
        RefreshScannerHint();
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
            Text = "扫码枪", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 24, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "扫码枪以键盘模式工作。请点击本页任意处后扫描条码进行测试。",
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(_scanHint);
        var simRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        simRow.Children.Add(_simCode);
        var sim = UiTheme.GhostButton("模拟扫码");
        sim.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_simCode.Text))
                _devices.SimulateBarcode(_simCode.Text.Trim());
        };
        simRow.Children.Add(sim);
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

        Content = new ScrollViewer { Content = root };
    }

    private void ReloadPorts()
    {
        var ports = ScaleWorker.ListPorts();
        _ports.ItemsSource = ports;
        var cur = _config.SerialPort;
        if (!string.IsNullOrEmpty(cur) && ports.Contains(cur))
            _ports.SelectedItem = cur;
        else if (ports.Count > 0)
            _ports.SelectedIndex = 0;
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
        _config.SerialPort = port;
        _config.AutoConnect = _auto.IsChecked == true;
        _devices.ConfigureAutoReconnect(_config);
        if (!string.IsNullOrEmpty(port))
            _devices.ConnectScale(port, _config.SerialBaud);
        UpdateScaleStatus();
        MessageBox.Show("已保存", "提示");
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

    private void RefreshScannerHint()
    {
        if (string.IsNullOrEmpty(_devices.LastScanCode))
            _scanHint.Text = _devices.IsScannerListening ? "扫码枪监听中…" : "等待扫码…";
        else
            _scanHint.Text = $"最近扫码：{_devices.LastScanCode}";
    }

}
