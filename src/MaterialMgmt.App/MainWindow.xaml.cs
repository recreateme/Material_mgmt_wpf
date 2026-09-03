using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.App.Views.Panels;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.App;

public partial class MainWindow : Window
{
    private readonly IAuthService _auth;
    private readonly IConfigService _config;
    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly IServiceProvider _sp;

    private readonly ListBox _navList = new();
    private readonly ContentControl _host = new();
    private readonly TextBlock _userName = new();
    private readonly TextBlock _userRole = new();
    private readonly TextBlock _scaleDot = new() { Text = "● 电子秤：未连接", Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBlock _scanDot = new() { Text = "● 扫码枪：空闲" };
    private readonly List<NavItem> _navItems = new();
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainWindow(
        IAuthService auth,
        IConfigService config,
        NavigationService nav,
        DeviceHub devices,
        IServiceProvider sp)
    {
        _auth = auth;
        _config = config;
        _nav = nav;
        _devices = devices;
        _sp = sp;

        Title = AppInfo.WindowTitle;
        MinWidth = 960;
        MinHeight = 600;
        ApplyGeometry();
        BuildUi();
        RegisterPages();
        PreviewKeyDown += OnPreviewKeyDown;

        _devices.ScaleStatusChanged += () => Dispatcher.Invoke(UpdateDeviceUi);
        _devices.ScannerStatusChanged += () => Dispatcher.Invoke(UpdateDeviceUi);
        _devices.BarcodeRouted += OnBarcodeRouted;
        _deviceTimer.Tick += (_, _) =>
        {
            _devices.Tick(_config);
            UpdateDeviceUi();
        };
        _deviceTimer.Start();

        Closed += async (_, _) =>
        {
            _deviceTimer.Stop();
            _devices.Dispose();
            var (w, h) = ((int)ActualWidth, (int)ActualHeight);
            if (w > 100 && h > 100)
                _config.SaveWindowSize(w, h, WindowState == WindowState.Maximized);
        };
    }

    public void OnLogin(CurrentUser user)
    {
        _userName.Text = user.DisplayName;
        _userRole.Text = RoleLabels.ToDisplay(user.Role);
        _nav.ClearCache();
        RebuildNav(user);
        _nav.Navigate(AppPage.Home);

        // 登录后：电子秤自动识别 + 扫码枪按配置连接
        _devices.BeginAutoConnectAfterLogin(_config);
        UpdateDeviceUi();
    }

    private void ApplyGeometry()
    {
        var (w, h) = _config.WindowSize;
        Width = Math.Max(960, w);
        Height = Math.Max(600, h);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_config.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void BuildUi()
    {
        var root = new DockPanel();

        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 7, 14, 7)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        _scaleDot.FontSize = 12;
        _scanDot.FontSize = 12;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(_scaleDot);
        statusRow.Children.Add(_scanDot);
        status.Child = statusRow;
        root.Children.Add(status);

        var nav = new Border
        {
            Width = 208,
            Background = UiTheme.Brush(UiTheme.NavBg)
        };
        DockPanel.SetDock(nav, Dock.Left);
        var navStack = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(16, 22, 16, 8) };
        header.Children.Add(new TextBlock
        {
            Text = "原料管理系统",
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = AppInfo.DisplayVersion,
            Foreground = UiTheme.Brush(UiTheme.NavText),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });
        _userName.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
        _userName.Margin = new Thickness(0, 14, 0, 0);
        _userName.FontSize = 13;
        _userRole.Foreground = UiTheme.Brush(UiTheme.NavText);
        _userRole.FontSize = 11;
        _userRole.Margin = new Thickness(0, 2, 0, 8);
        header.Children.Add(_userName);
        header.Children.Add(_userRole);
        DockPanel.SetDock(header, Dock.Top);

        var footer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var pw = UiTheme.NavFooterButton("修改密码");
        pw.Click += (_, _) =>
        {
            var u = _auth.CurrentUser;
            if (u is null) return;
            new ChangePasswordDialog(_auth, u.Id) { Owner = this }.ShowDialog();
        };
        var logout = UiTheme.NavFooterButton("退出登录", logout: true);
        logout.Click += (_, _) => DoLogout();
        footer.Children.Add(pw);
        footer.Children.Add(logout);

        _navList.Background = Brushes.Transparent;
        _navList.BorderThickness = new Thickness(0);
        _navList.Foreground = UiTheme.Brush(UiTheme.NavText);
        if (TryFindResource("NavListBoxItemStyle") is Style itemStyle)
            _navList.ItemContainerStyle = itemStyle;
        _navList.SelectionChanged += OnNavChanged;

        navStack.Children.Add(header);
        navStack.Children.Add(footer);
        navStack.Children.Add(_navList);
        nav.Child = navStack;
        root.Children.Add(nav);

        _host.Background = UiTheme.Brush(UiTheme.PageBg);
        root.Children.Add(_host);
        Content = root;
        _nav.AttachHost(_host);
        _nav.Navigated += OnNavigated;
    }

    private bool _syncingNav;

    private void OnNavigated(AppPage page)
    {
        var highlight = page == AppPage.FormulaEditor ? AppPage.Formulas : page;
        var item = _navItems.FirstOrDefault(x => x.Page == highlight);
        if (item is null) return;
        _syncingNav = true;
        try { _navList.SelectedItem = item; }
        finally { _syncingNav = false; }
    }

    private void RegisterPages()
    {
        _nav.Register(AppPage.Home, () => (UserControl)_sp.GetService(typeof(HomePanel))!);
        _nav.Register(AppPage.Materials, () => (UserControl)_sp.GetService(typeof(MaterialsPanel))!);
        _nav.Register(AppPage.Formulas, () => (UserControl)_sp.GetService(typeof(FormulasPanel))!);
        _nav.Register(AppPage.FormulaEditor, () => (UserControl)_sp.GetService(typeof(FormulaEditorPanel))!);
        _nav.Register(AppPage.SessionSelect, () => (UserControl)_sp.GetService(typeof(SessionSelectPanel))!);
        _nav.Register(AppPage.WeighSession, () => (UserControl)_sp.GetService(typeof(WeighSessionPanel))!);
        _nav.Register(AppPage.AdminHistory, () => (UserControl)_sp.GetService(typeof(AdminHistoryPanel))!);
        _nav.Register(AppPage.OperatorHistory, () => (UserControl)_sp.GetService(typeof(OperatorHistoryPanel))!);
        _nav.Register(AppPage.UserMgmt, () => (UserControl)_sp.GetService(typeof(UserMgmtPanel))!);
        _nav.Register(AppPage.Settings, () => (UserControl)_sp.GetService(typeof(SettingsPanel))!);
    }

    private void RebuildNav(CurrentUser user)
    {
        _navItems.Clear();
        void Add(string title, AppPage page)
        {
            if (NavigationService.CanAccess(page, user))
                _navItems.Add(new NavItem(title, page));
        }
        Add("首页", AppPage.Home);
        Add("原材料", AppPage.Materials);
        Add("配方管理", AppPage.Formulas);
        Add("开始称量", AppPage.SessionSelect);
        Add("工单历史", AppPage.AdminHistory);
        Add("我的工单", AppPage.OperatorHistory);
        Add("用户管理", AppPage.UserMgmt);
        Add("系统设置", AppPage.Settings);
        _navList.ItemsSource = null;
        _navList.ItemsSource = _navItems;
        _navList.DisplayMemberPath = "Title";
        if (_navItems.Count > 0) _navList.SelectedIndex = 0;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav) return;
        if (_navList.SelectedItem is not NavItem item) return;
        var user = _auth.CurrentUser;
        if (user is null || !NavigationService.CanAccess(item.Page, user)) return;
        _nav.Navigate(item.Page);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_devices.ShouldAcceptHidKeys) return;
        var text = e.Key == Key.Enter || e.Key == Key.Return ? null : KeyToText(e);
        _devices.Barcode.OnKey(e.Key is Key.Enter or Key.Return, text);
    }

    private static string? KeyToText(KeyEventArgs e)
    {
        // 仅处理可打印字符；扫码枪通常输出数字/字母
        if (e.Key >= Key.D0 && e.Key <= Key.D9) return ((char)('0' + (e.Key - Key.D0))).ToString();
        if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) return ((char)('0' + (e.Key - Key.NumPad0))).ToString();
        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            var c = (char)('A' + (e.Key - Key.A));
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? c.ToString() : char.ToLowerInvariant(c).ToString();
        }
        if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "_" : "-";
        if (e.Key == Key.OemPeriod) return ".";
        return null;
    }

    private void OnBarcodeRouted(string code, bool simulated)
    {
        if (_host.Content is IBarcodeHandler handler)
            handler.OnBarcode(code, simulated);
        else
            _scanDot.Text = $"● 扫码：{code}";
    }

    private void UpdateDeviceUi()
    {
        _scaleDot.Text = _devices.IsScaleConnected
            ? $"● 电子秤：已连接（{_devices.ConnectedPort}）"
            : _devices.IsDiscovering || _devices.ScaleLastError.Contains("识别", StringComparison.Ordinal)
                ? $"● 电子秤：{_devices.ScaleLastError}"
                : string.IsNullOrEmpty(_devices.ScaleLastError)
                    ? "● 电子秤：未连接"
                    : $"● 电子秤：未连接 — {_devices.ScaleLastError}";
        _scaleDot.Foreground = UiTheme.Brush(_devices.IsScaleConnected ? UiTheme.Success : UiTheme.Danger);
        var scanLabel = _devices.ScannerSourceLabel;
        if (!string.IsNullOrEmpty(_devices.LastScanCode) && _devices.IsScannerListening)
            _scanDot.Text = $"● 扫码：{_devices.LastScanCode}（{scanLabel}）";
        else if (_devices.IsScannerSerialConnected)
            _scanDot.Text = $"● 扫码枪：已连接（{scanLabel}）";
        else if (!string.IsNullOrEmpty(_devices.ScannerLastError))
            _scanDot.Text = $"● 扫码枪：{_devices.ScannerLastError}";
        else
            _scanDot.Text = _devices.IsScannerListening
                ? $"● 扫码枪：监听中（{scanLabel}）"
                : $"● 扫码枪：空闲（{scanLabel}）";
        var scanOk = _devices.IsScannerSerialConnected
                     || scanLabel.Contains("HID", StringComparison.Ordinal)
                     || scanLabel.Contains("关闭", StringComparison.Ordinal);
        _scanDot.Foreground = UiTheme.Brush(scanOk && string.IsNullOrEmpty(_devices.ScannerLastError)
            ? UiTheme.Success
            : UiTheme.Warning);
    }

    private void DoLogout()
    {
        _devices.DisconnectScale();
        _auth.Logout();
        _nav.ClearCache();
        Hide();

        // 退出登录期间避免「无窗口」触发应用退出
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var login = new LoginWindow(_auth) { Owner = this };
        if (login.ShowDialog() == true && login.LoggedInUser is not null)
        {
            OnLogin(login.LoggedInUser);
            Show();
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        else
        {
            Application.Current.Shutdown(0);
        }
    }

    private sealed record NavItem(string Title, AppPage Page);
}
