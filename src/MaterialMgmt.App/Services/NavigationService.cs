using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Services;

/// <summary>页面身份。侧栏和称量窗都用这个枚举，不使用 URL。</summary>
public enum AppPage
{
    Home = 0,
    Materials = 1,
    Formulas = 2,
    FormulaEditor = 3,
    SessionSelect = 4,
    WeighSession = 5,
    AdminHistory = 6,
    OperatorHistory = 7,
    UserMgmt = 8,
    Settings = 9
}

/// <summary>
/// 把 <see cref="AppPage"/> 显示到一个 ContentControl 里，并按角色拒绝无权页面。
/// 面板第一次打开时创建，之后放在缓存里，直到退出登录 ClearCache。
/// </summary>
public sealed class NavigationService
{
    private readonly Dictionary<AppPage, Func<UserControl>> _factories = new();
    private readonly Dictionary<AppPage, UserControl> _cache = new();
    private ContentControl? _host;

    public event Action<AppPage>? Navigated;

    public AppPage Current { get; private set; } = AppPage.Home;

    public void AttachHost(ContentControl host) => _host = host;

    public void Register(AppPage page, Func<UserControl> factory) => _factories[page] = factory;

    public void ClearCache()
    {
        _cache.Clear();
    }

    public void Navigate(AppPage page, Action<UserControl>? configure = null)
    {
        if (_host is null) throw new InvalidOperationException("导航宿主未附加");
        if (!_factories.TryGetValue(page, out var factory))
            throw new InvalidOperationException($"未注册页面：{page}");

        if (!_cache.TryGetValue(page, out var view))
        {
            view = factory();
            _cache[page] = view;
        }

        configure?.Invoke(view);
        if (view is IPageLifecycle life) life.OnShow();
        _host.Content = view;
        Current = page;
        Navigated?.Invoke(page);
    }

    public static bool CanAccess(AppPage page, CurrentUser user) => page switch
    {
        AppPage.Home => RolePermissions.CanAccessHome(user),
        AppPage.Settings => RolePermissions.CanAccessSettings(user),
        AppPage.UserMgmt => RolePermissions.CanManageUsers(user),
        AppPage.Materials or AppPage.Formulas or AppPage.FormulaEditor
            or AppPage.AdminHistory or AppPage.OperatorHistory
            or AppPage.SessionSelect or AppPage.WeighSession
            => RolePermissions.CanAccessBusinessModules(user),
        _ => false
    };
}

public interface IPageLifecycle
{
    void OnShow();
}

public interface IBarcodeHandler
{
    void OnBarcode(string code, bool simulated = false);
}
