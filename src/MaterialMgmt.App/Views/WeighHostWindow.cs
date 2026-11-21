using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views;

/// <summary>称量专用子窗：开始称量 / 称量中 / 我的工单。</summary>
public sealed class WeighHostWindow : Window
{
    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly ListBox _navList = new();
    private readonly ContentControl _host = new();
    private readonly TextBlock _scaleDot = new() { Text = "● 电子秤：—", Margin = new Thickness(0, 0, 16, 0), FontSize = 12 };
    private readonly TextBlock _scanDot = new() { Text = "● 扫码枪：—", FontSize = 12 };
    private readonly List<NavItem> _items = new();
    private bool _syncingNav;

    public ContentControl Host => _host;

    public WeighHostWindow(NavigationService nav, DeviceHub devices, CurrentUser user)
    {
        _nav = nav;
        _devices = devices;
        Title = $"称量工作台 — {user.DisplayName}";
        WindowSizing.Fit(this, 1100, 720, 880, 560);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildUi();
        _nav.AttachHost(_host);
        _nav.Navigated += OnNavigated;
        PreviewKeyDown += OnPreviewKeyDown;
        _devices.ScaleStatusChanged += () => Dispatcher.Invoke(UpdateDeviceUi);
        _devices.ScannerStatusChanged += () => Dispatcher.Invoke(UpdateDeviceUi);
        UpdateDeviceUi();
        RebuildNav();
    }

    private void BuildUi()
    {
        var root = new DockPanel();

        var status = new Border
        {
            Background = UiTheme.Brush(UiTheme.PanelAlt),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 7, 14, 7)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(_scaleDot);
        statusRow.Children.Add(_scanDot);
        statusRow.Children.Add(new TextBlock
        {
            Text = "  ·  称量进行中时扫码仅进入本窗",
            FontSize = UiTheme.FontCaption,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            VerticalAlignment = VerticalAlignment.Center
        });
        status.Child = statusRow;
        root.Children.Add(status);

        var nav = new Border { Width = 188, Background = UiTheme.Brush(UiTheme.NavBg) };
        DockPanel.SetDock(nav, Dock.Left);
        var navStack = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(14, 20, 14, 8) };
        header.Children.Add(new TextBlock
        {
            Text = "称量工作台",
            Foreground = UiTheme.Brush(UiTheme.NavTitle),
            FontWeight = FontWeights.Bold,
            FontSize = 15
        });
        header.Children.Add(new TextBlock
        {
            Text = "与主窗共享设备",
            Foreground = UiTheme.Brush(UiTheme.NavText),
            FontSize = UiTheme.FontCaption,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);

        _navList.Background = Brushes.Transparent;
        _navList.BorderThickness = new Thickness(0);
        _navList.Foreground = UiTheme.Brush(UiTheme.NavText);
        if (TryFindResource("NavListBoxItemStyle") is Style itemStyle)
            _navList.ItemContainerStyle = itemStyle;
        _navList.SelectionChanged += OnNavChanged;

        navStack.Children.Add(header);
        navStack.Children.Add(_navList);
        nav.Child = navStack;
        root.Children.Add(nav);

        _host.Background = UiTheme.Brush(UiTheme.PageBg);
        root.Children.Add(_host);
        Content = root;
    }

    private void RebuildNav()
    {
        _items.Clear();
        _items.Add(new NavItem("开始称量", AppPage.SessionSelect));
        _items.Add(new NavItem("我的工单", AppPage.OperatorHistory));
        _navList.ItemsSource = null;
        _navList.ItemsSource = _items;
        _navList.DisplayMemberPath = nameof(NavItem.Title);
        _navList.SelectedIndex = 0;
    }

    private void OnNavigated(AppPage page)
    {
        var highlight = page == AppPage.WeighSession ? AppPage.SessionSelect : page;
        var item = _items.FirstOrDefault(x => x.Page == highlight);
        if (item is null) return;
        _syncingNav = true;
        try { _navList.SelectedItem = item; }
        finally { _syncingNav = false; }
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav) return;
        if (_navList.SelectedItem is not NavItem item) return;
        if (_nav.Current == AppPage.WeighSession)
        {
            MessageBox.Show(this, "请先完成或取消当前称量后再切换页面。", "提示");
            _syncingNav = true;
            try { _navList.SelectedItem = _items.First(x => x.Page == AppPage.SessionSelect); }
            finally { _syncingNav = false; }
            return;
        }
        _nav.Navigate(item.Page);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_devices.ShouldAcceptHidKeys) return;
        var text = e.Key is Key.Enter or Key.Return ? null : KeyToText(e);
        _devices.Barcode.OnKey(e.Key is Key.Enter or Key.Return, text);
    }

    private static string? KeyToText(KeyEventArgs e)
    {
        if (e.Key >= Key.D0 && e.Key <= Key.D9) return ((char)('0' + (e.Key - Key.D0))).ToString();
        if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) return ((char)('0' + (e.Key - Key.NumPad0))).ToString();
        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            var c = (char)('A' + (e.Key - Key.A));
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? c.ToString() : char.ToLowerInvariant(c).ToString();
        }
        if (e.Key is Key.OemMinus or Key.Subtract)
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "_" : "-";
        if (e.Key == Key.OemPeriod) return ".";
        return null;
    }

    private void UpdateDeviceUi()
    {
        _scaleDot.Text = _devices.IsScaleConnected
            ? $"● 电子秤：已连接（{_devices.ConnectedPort}）"
            : "● 电子秤：未连接";
        _scaleDot.Foreground = UiTheme.Brush(_devices.IsScaleConnected ? UiTheme.Success : UiTheme.Danger);
        var label = _devices.ScannerSourceLabel;
        _scanDot.Text = !string.IsNullOrEmpty(_devices.LastScanCode) && _devices.IsScannerListening
            ? $"● 扫码：{_devices.LastScanCode}（{label}）"
            : $"● 扫码枪：{label}";
    }

    private sealed record NavItem(string Title, AppPage Page);
}
