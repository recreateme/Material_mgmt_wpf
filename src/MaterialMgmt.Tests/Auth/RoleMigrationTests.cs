using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.Tests.Auth;

public class RoleMigrationTests
{
    [Fact]
    public void Plan_MapsAdminAndOperator_DeletesRoot()
    {
        var users = new[]
        {
            new RoleMigration.UserSnap(1, "root", Roles.LegacyRoot),
            new RoleMigration.UserSnap(4, "admin", Roles.LegacyAdmin),
            new RoleMigration.UserSnap(5, "操作员1", Roles.LegacyOperator),
            new RoleMigration.UserSnap(6, "饶子健", Roles.LegacyOperator),
        };

        var plan = RoleMigration.Plan(users);
        Assert.Null(plan.Error);
        Assert.Contains(plan.Updates, x => x.Id == 4 && x.NewRole == Roles.SystemAdmin);
        Assert.Contains(plan.Updates, x => x.Id == 5 && x.NewRole == Roles.Business);
        Assert.Contains(plan.Updates, x => x.Id == 6 && x.NewRole == Roles.Business);
        Assert.Equal(new[] { 1 }, plan.DeleteIds);
    }

    [Fact]
    public void Plan_BlocksDeleteRoot_WhenNoAdminRemains()
    {
        var users = new[]
        {
            new RoleMigration.UserSnap(1, "root", Roles.LegacyRoot),
            new RoleMigration.UserSnap(2, "op", Roles.LegacyOperator),
        };

        var plan = RoleMigration.Plan(users);
        Assert.NotNull(plan.Error);
        Assert.Contains("系统管理员", plan.Error);
    }

    [Fact]
    public void Plan_Idempotent_WhenAlreadyMigrated()
    {
        var users = new[]
        {
            new RoleMigration.UserSnap(4, "admin", Roles.SystemAdmin),
            new RoleMigration.UserSnap(5, "op", Roles.Business),
        };

        var plan = RoleMigration.Plan(users);
        Assert.Null(plan.Error);
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.DeleteIds);
    }

    [Fact]
    public void NormalizeRole_LegacyAndNew()
    {
        Assert.Equal(Roles.SystemAdmin, RoleMigration.NormalizeRole(Roles.LegacyAdmin));
        Assert.Equal(Roles.Business, RoleMigration.NormalizeRole(Roles.LegacyOperator));
        Assert.Null(RoleMigration.NormalizeRole(Roles.LegacyRoot));
        Assert.Equal(Roles.SystemAdmin, RoleMigration.NormalizeRole(Roles.SystemAdmin));
    }
}
