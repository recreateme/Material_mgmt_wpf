using Microsoft.Extensions.DependencyInjection;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("zhangsan", "", "请输入密码")]
    [InlineData("zhangsan", "12345", "至少 6 位")]
    [InlineData("zhangsan", "ZhangSan", "用户名")]
    [InlineData("zhangsan", "88888888", "出厂默认密码")]
    [InlineData("zhangsan", "operator123", "出厂默认密码")]
    public void Validate_RejectsWithReason(string user, string password, string reasonPart)
        => Assert.Contains(reasonPart, PasswordPolicy.Validate(user, password));

    [Theory]
    [InlineData("zhangsan", "123456")]
    [InlineData("zhangsan", "Lab_2026")]
    public void Validate_AcceptsCompliantPassword(string user, string password)
        => Assert.Null(PasswordPolicy.Validate(user, password));

    [Fact]
    public void IsSeedDefault_OnlyMatchesOwnSeedPassword()
    {
        Assert.True(PasswordPolicy.IsSeedDefault("ADMIN", "88888888"));
        Assert.False(PasswordPolicy.IsSeedDefault("operator", "88888888"));
        Assert.False(PasswordPolicy.IsSeedDefault("zhangsan", "88888888"));
    }

    [Fact]
    public async Task ChangePassword_EnforcesPolicy_ThenNewPasswordWorks()
    {
        var root = Path.Combine(Path.GetTempPath(), "mm_pwd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMaterialMgmtInfrastructure(root);
            await using var sp = services.BuildServiceProvider();
            await sp.GetRequiredService<DbBootstrap>().InitializeAsync();
            var auth = sp.GetRequiredService<IAuthService>();

            var me = auth.Login("operator", "operator123")!;
            Assert.True(auth.IsDefaultPassword("operator", "operator123"));
            Assert.Throws<InvalidOperationException>(() => auth.ChangePassword(me.Id, "operator123", "operator"));
            Assert.Throws<InvalidOperationException>(() => auth.ChangePassword(me.Id, "operator123", "88888888"));
            Assert.Throws<InvalidOperationException>(() => auth.ChangePassword(me.Id, "wrong-old", "Lab_2026"));

            auth.ChangePassword(me.Id, "operator123", "Lab_2026");
            auth.Logout();
            Assert.Null(auth.Login("operator", "operator123"));
            Assert.NotNull(auth.Login("operator", "Lab_2026"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
