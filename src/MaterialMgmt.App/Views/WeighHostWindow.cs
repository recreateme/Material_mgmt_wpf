using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views;

/// <summary>称量专用子窗：开始称量 / 称量中 / 我的工单。</summary>
public sealed class WeighHostWindow : Window
{
    private const string GeometrySection = "weigh_window";

    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly IConfigService _config;
    private readonly NavMenu _navMenu = new();
    private readonly ContentControl _host = new();

    public ContentControl Host => _host;

    public WeighHostWindow(NavigationService nav, DeviceHub devices, IConfigService config, CurrentUser user)
    {
        _nav = nav;
        _devices = devices;
        _config = config;
        Title = $"称量工作台 — {user.DisplayName}";
        RestoreGeometry();
        BuildUi();
        _nav.AttachHost(_host);
        _nav.Navigated += OnNavigated;
        PreviewKeyDown += (_, e) => HidKeyMapper.Forward(_devices, e);
        Closing += (_, _) => SaveGeometry();
        _navMenu.SetItems(new[]
        {
            new NavMenuItem("开始称量", NavGlyphs.StartWeigh, AppPage.SessionSelect),
            new NavMenuItem("我的工单", NavGlyphs.MyOrders, AppPage.OperatorHistory)
        });
    }

    private void BuildUi()
    {
        var root = new DockPanel();

        var status = new DeviceStatusBar(_devices, _config, note: "称量进行中时扫码仅进入本窗");
        DockPanel.SetDock(status, Dock.Bottom);
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

        _navMenu.PageRequested += OnPageRequested;

        navStack.Children.Add(header);
        navStack.Children.Add(_navMenu);
        nav.Child = navStack;
        root.Children.Add(nav);

        _host.Background = UiTheme.Brush(UiTheme.PageBg);
        root.Children.Add(_host);
        Content = root;
    }

    private void OnNavigated(AppPage page)
        => _navMenu.Highlight(page == AppPage.WeighSession ? AppPage.SessionSelect : page);

    private void OnPageRequested(AppPage page)
    {
        if (_nav.Current == AppPage.WeighSession)
        {
            UiFeedback.Warn("请先完成、暂停（左上角“暂停并返回”）或取消当前称量后再切换页面。");
            _navMenu.Highlight(AppPage.SessionSelect);
            return;
        }
        _nav.Navigate(page);
    }

    /// <summary>恢复上次的大小和位置；没有记录或已不在任何屏幕内（换了显示器）时按工作区自适应居中。</summary>
    private void RestoreGeometry()
    {
        WindowSizing.Fit(this, 1100, 720, 880, 560);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        double Read(string key) => double.TryParse(_config.GetValue(GeometrySection, key, ""),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        var (left, top, width, height) = (Read("left"), Read("top"), Read("width"), Read("height"));
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height)) return;
        if (width < MinWidth || height < MinHeight) return;

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.Contains(new Rect(left, top, width, height))) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (left, top, width, height);
        if (_config.GetValue(GeometrySection, "maximized", "false") == "true")
            WindowState = WindowState.Maximized;
    }

    private void SaveGeometry()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (r.IsEmpty || r.Width < 100 || r.Height < 100) return;
        string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
        try
        {
            _config.SetValue(GeometrySection, "left", F(r.Left));
            _config.SetValue(GeometrySection, "top", F(r.Top));
            _config.SetValue(GeometrySection, "width", F(r.Width));
            _config.SetValue(GeometrySection, "height", F(r.Height));
            _config.SetValue(GeometrySection, "maximized", WindowState == WindowState.Maximized ? "true" : "false");
        }
        catch (IOException)
        {
            // 配置文件被占用时放弃保存，不影响关闭
        }
    }
}
