using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Views;
using MaterialMgmt.App.Views.Panels;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MaterialMgmt.App.Services;

/// <summary>业务人员称量子窗：单实例、独立导航、与主窗共享 DeviceHub。</summary>
public sealed class WeighWorkspace
{
    private readonly IServiceProvider _sp;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;

    private WeighHostWindow? _window;
    private NavigationService? _nav;

    public WeighWorkspace(IServiceProvider sp, IAuthService auth, IAuditLog audit)
    {
        _sp = sp;
        _auth = auth;
        _audit = audit;
    }

    public bool IsOpen => _window is not null;
    public Window? HostWindow => _window;
    public bool ExclusiveBarcode { get; private set; }

    public IBarcodeHandler? CurrentBarcodeHandler =>
        _window?.Host.Content as IBarcodeHandler;

    public void SetExclusiveBarcode(bool exclusive) => ExclusiveBarcode = exclusive;

    public void Open(AppPage page = AppPage.SessionSelect, Action<UserControl>? configure = null)
    {
        if (_auth.CurrentUser is not { } user || !user.IsBusiness())
        {
            MessageBox.Show("仅业务人员可打开称量窗。", "提示");
            return;
        }

        EnsureWindow(user);
        _nav!.Navigate(page, configure);
        _window!.Activate();
        _window.Focus();
    }

    public void OpenResume(int workOrderId, int formulaId) =>
        Open(AppPage.WeighSession, v => ((WeighSessionPanel)v).Load(workOrderId, formulaId));

    public void Close()
    {
        try { _window?.Close(); }
        catch { /* ignore */ }
    }

    private void EnsureWindow(CurrentUser user)
    {
        if (_window is not null) return;

        _nav = new NavigationService();
        RegisterWeighPages(_nav);
        _window = new WeighHostWindow(_nav, _sp.GetRequiredService<DeviceHub>(),
            _sp.GetRequiredService<IConfigService>(), user);
        _window.Closed += OnWindowClosed;
        _window.Show();
        _audit.Log("weigh_window", "打开称量窗", user.Username);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is WeighHostWindow w)
            w.Closed -= OnWindowClosed;
        var user = _auth.CurrentUser?.Username ?? "";
        _window = null;
        _nav = null;
        ExclusiveBarcode = false;
        _audit.Log("weigh_window", "关闭称量窗", user);
    }

    private void RegisterWeighPages(NavigationService nav)
    {
        nav.Register(AppPage.SessionSelect,
            () => ActivatorUtilities.CreateInstance<SessionSelectPanel>(_sp, nav));
        nav.Register(AppPage.WeighSession,
            () => ActivatorUtilities.CreateInstance<WeighSessionPanel>(_sp, nav));
        nav.Register(AppPage.OperatorHistory,
            () => ActivatorUtilities.CreateInstance<OperatorHistoryPanel>(_sp, nav));
    }
}
