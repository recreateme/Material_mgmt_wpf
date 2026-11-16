using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.Tests.Auth;

public class NavigationAccessTests
{
    private static CurrentUser User(string role) => new()
    {
        Id = 1, Username = "u", DisplayName = "U", Role = role
    };

    [Fact]
    public void SystemAdmin_Permissions()
    {
        var u = User(Roles.SystemAdmin);
        Assert.True(RolePermissions.CanAccessHome(u));
        Assert.True(RolePermissions.CanAccessSettings(u));
        Assert.True(RolePermissions.CanManageUsers(u));
        Assert.False(RolePermissions.CanAccessBusinessModules(u));
    }

    [Fact]
    public void Business_Permissions()
    {
        var u = User(Roles.Business);
        Assert.True(RolePermissions.CanAccessBusinessModules(u));
        Assert.True(RolePermissions.CanAccessSettings(u));
        Assert.False(RolePermissions.CanManageUsers(u));
    }
}
