using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.App.Views.Panels;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App;

public partial class MainWindow : Window
{
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly IConfigService _config;
    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly WeighWorkspace _weigh;
    private readonly IServiceProvider _sp;

    private readonly NavMenu _navMenu = new();
    private readonly ContentControl _host = new();
    private readonly TextBlock _userName = new();
    private readonly TextBlock _userRole = new();
    private DeviceStatusBar _statusBar = null!;
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainWindow(
        IAuthService auth,
        IAuditLog audit,
        IConfigService config,
        NavigationService nav,
        DeviceHub devices,
        WeighWorkspace weigh,
        IServiceProvider sp)
    {
        _auth = auth;
        _audit = audit;
        _config = config;
        _nav = nav;
        _devices = devices;
        _weigh = weigh;
        _sp = sp;

        Title = AppInfo.WindowTitle;
        ApplyGeometry();
        BuildUi();
        RegisterPages();
        PreviewKeyDown += (_, e) => HidKeyMapper.Forward(_devices, e);
        WireBarcodeRouting();

        _deviceTimer.Tick += (_, _) => _devices.Tick(_config);
        _deviceTimer.Start();

        Closing += (_, e) =>
        {
            if (_weigh.IsOpen)
                _weigh.Close();
        };
        Closed += (_, _) =>
        {
            _deviceTimer.Stop();
            _weigh.Close();
            _devices.Dispose();
            var (w, h) = ((int)ActualWidth, (int)ActualHeight);
            if (w > 100 && h > 100)
                _config.SaveWindowSize(w, h, WindowState == WindowState.Maximized);
        };
    }

    private void WireBarcodeRouting()
    {
        _devices.IsBarcodeExclusive = () => _weigh.ExclusiveBarcode;
        _devices.ResolveBarcodeHandler = ResolveBarcodeHandler;
    }

    private IBarcodeHandler? ResolveBarcodeHandler()
    {
        if (_weigh.ExclusiveBarcode)
            return _weigh.CurrentBarcodeHandler;

        var active = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (_weigh.IsOpen && active is not null && ReferenceEquals(active, _weigh.HostWindow))
            return _weigh.CurrentBarcodeHandler;
        if (active is not null && ReferenceEquals(active, this))
            return _host.Content as IBarcodeHandler;
        if (_weigh.IsOpen && _weigh.HostWindow?.IsActive == true)
            return _weigh.CurrentBarcodeHandler;
        return _host.Content as IBarcodeHandler;
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
        _statusBar.Refresh();
    }

    private void ApplyGeometry()
    {
        var (w, h) = _config.WindowSize;
        // 设计尺寸（保存的窗口大小）不超过工作区 90%，小屏 / 高缩放下不会被截断
        WindowSizing.Fit(this, Math.Max(960, w), Math.Max(600, h), 960, 600);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_config.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void BuildUi()
    {
        var root = new DockPanel();

        _statusBar = new DeviceStatusBar(_devices, _config, () => _weigh.ExclusiveBarcode);
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        root.Children.Add(_statusBar);

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
            Foreground = UiTheme.Brush(UiTheme.NavTitle),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = AppInfo.DisplayVersion,
            Foreground = UiTheme.Brush(UiTheme.NavText),
            FontSize = UiTheme.FontCaption,
            Margin = new Thickness(0, 2, 0, 0)
        });
        _userName.Foreground = UiTheme.Brush(UiTheme.NavUser);
        _userName.Margin = new Thickness(0, 14, 0, 0);
        _userName.FontSize = 13;
        _userRole.Foreground = UiTheme.Brush(UiTheme.NavText);
        _userRole.FontSize = UiTheme.FontCaption;
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

        _navMenu.PageRequested += OnPageRequested;

        navStack.Children.Add(header);
        navStack.Children.Add(footer);
        navStack.Children.Add(_navMenu);
        nav.Child = navStack;
        root.Children.Add(nav);

        _host.Background = UiTheme.Brush(UiTheme.PageBg);
        root.Children.Add(_host);
        Content = root;
        _nav.AttachHost(_host);
        _nav.Navigated += OnNavigated;
    }

    private void OnNavigated(AppPage page)
        => _navMenu.Highlight(page == AppPage.FormulaEditor ? AppPage.Formulas : page);

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
        var items = new List<NavMenuItem>();
        void Add(string title, string glyph, AppPage page, string group)
        {
            if (NavigationService.CanAccess(page, user))
                items.Add(new NavMenuItem(title, glyph, page, Group: group));
        }

        // 称量相关页在独立称量窗；主窗的“开始称量 / 我的工单”负责打开称量窗
        Add("首页", NavGlyphs.Home, AppPage.Home, "日常");
        if (user.IsBusiness())
        {
            items.Add(new NavMenuItem("开始称量", NavGlyphs.StartWeigh,
                Invoke: () => _weigh.Open(AppPage.SessionSelect), Group: "日常"));
            items.Add(new NavMenuItem("我的工单", NavGlyphs.MyOrders,
                Invoke: () => _weigh.Open(AppPage.OperatorHistory), Group: "日常"));
        }
        Add("原材料", NavGlyphs.Materials, AppPage.Materials, "管理");
        Add("配方管理", NavGlyphs.Formulas, AppPage.Formulas, "管理");
        Add("工单历史", NavGlyphs.History, AppPage.AdminHistory, "管理");
        Add("用户管理", NavGlyphs.Users, AppPage.UserMgmt, "管理");
        Add("系统设置", NavGlyphs.Settings, AppPage.Settings, "管理");
        _navMenu.SetItems(items);
    }

    private void OnPageRequested(AppPage page)
    {
        var user = _auth.CurrentUser;
        if (user is null || !NavigationService.CanAccess(page, user))
        {
            _navMenu.Highlight(_nav.Current);
            return;
        }
        _nav.Navigate(page);
    }

    private void DoLogout()
    {
        _weigh.Close();
        _devices.DisconnectScale();
        _auth.Logout();
        _nav.ClearCache();
        Hide();

        // 退出登录期间避免「无窗口」触发应用退出
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var login = new LoginWindow(_auth, _config, _audit) { Owner = this };
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
}
