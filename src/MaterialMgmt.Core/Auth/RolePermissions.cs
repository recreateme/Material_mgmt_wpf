namespace MaterialMgmt.Core.Auth;

/// <summary>v4.2 固定两角色的页面能力（与 NavigationService 对齐）。</summary>
public static class RolePermissions
{
    public static bool CanAccessSettings(CurrentUser _) => true;
    public static bool CanAccessHome(CurrentUser _) => true;
    public static bool CanManageUsers(CurrentUser user) => user.IsSystemAdmin();
    public static bool CanAccessBusinessModules(CurrentUser user) => user.IsBusiness();
}
