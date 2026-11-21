using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Services;

public class UserServiceTests : TestHost
{
    private const string Pw = "Qwe-7788";
    private UserService Svc => Get<UserService>();

    [Fact]
    public void CreateBusiness_DefaultsDisplayName_AndCanLogIn()
    {
        var u = Svc.CreateBusiness(" zhang ", " ", Pw);
        Assert.Equal(("zhang", "zhang", Roles.Business, true), (u.Username, u.DisplayName, u.Role, u.IsActive));
        Assert.NotNull(Get<IAuthService>().Login("zhang", Pw));
        Assert.Contains(Svc.BusinessUsers(), x => x.Id == u.Id);
    }

    [Fact]
    public void CreateBusiness_RejectsDuplicateAndWeakPassword()
    {
        Svc.CreateBusiness("li", "李", Pw);
        Assert.True(Svc.UsernameExists(" li "));
        Assert.Throws<InvalidOperationException>(() => Svc.CreateBusiness("li", null, Pw));
        Assert.Throws<InvalidOperationException>(() => Svc.CreateBusiness("wang", null, "123"));
        Assert.Throws<InvalidOperationException>(() => Svc.CreateBusiness("wang123", null, "wang123"));
    }

    [Fact]
    public void ResetPassword_AppliesPolicy_AndChangesLogin()
    {
        var u = Svc.CreateBusiness("zhao", null, Pw);
        Assert.Throws<InvalidOperationException>(() => Svc.ResetPassword(u.Id, "12"));
        Svc.ResetPassword(u.Id, "Asd-5566");
        var auth = Get<IAuthService>();
        Assert.Null(auth.Login("zhao", Pw));
        Assert.NotNull(auth.Login("zhao", "Asd-5566"));
    }

    [Fact]
    public void AdminAccounts_CannotBeMaintained()
    {
        var admin = Svc.BusinessUsers().Count; // 种子数据里的管理员不在业务人员列表中
        using var db = Factory.CreateDbContext();
        var adminId = db.Users.First(u => u.Role != Roles.Business).Id;

        Assert.Throws<InvalidOperationException>(() => Svc.ToggleActive(adminId));
        Assert.Throws<InvalidOperationException>(() => Svc.ResetPassword(adminId, "Asd-5566"));
        Assert.Throws<InvalidOperationException>(() => Svc.UpdateDisplayName(adminId, "x"));
        Assert.Empty(Svc.Delete(new[] { adminId }, unlinkOrders: true));
        Assert.Equal(admin, Svc.BusinessUsers().Count);
    }

    [Fact]
    public void ToggleActive_AndDisable()
    {
        var u = Svc.CreateBusiness("qian", null, Pw);
        Assert.False(Svc.ToggleActive(u.Id).IsActive);
        Assert.True(Svc.ToggleActive(u.Id).IsActive);

        Assert.Single(Svc.Disable(new[] { u.Id }));
        Assert.Empty(Svc.Disable(new[] { u.Id }));
    }

    [Fact]
    public void Delete_WithOrders_RequiresUnlink_AndKeepsOrders()
    {
        var linked = Svc.CreateBusiness("sun", null, Pw);
        var free = Svc.CreateBusiness("zhou", null, Pw);
        var (f, _) = AddFormula("FML-T00001", (AddMaterial("MAT-T00001", "甲"), 1, 2));
        var wo = AddOrder(f.Id, "completed", linked.Id);

        Assert.Equal(new Dictionary<int, int> { [linked.Id] = 1 }, Svc.OrderCounts(new[] { linked.Id, free.Id }));
        Assert.Throws<InvalidOperationException>(() => Svc.Delete(new[] { linked.Id, free.Id }, unlinkOrders: false));
        Assert.Equal(2, Svc.BusinessUsers().Count(u => u.Id == linked.Id || u.Id == free.Id));

        var deleted = Svc.Delete(new[] { linked.Id, free.Id }, unlinkOrders: true);
        Assert.Equal(2, deleted.Count);
        using var db = Factory.CreateDbContext();
        var order = db.WorkOrders.Single(w => w.Id == wo.Id);
        Assert.Null(order.OperatorId);
        Assert.Equal("tester", order.Operator);
    }
}
