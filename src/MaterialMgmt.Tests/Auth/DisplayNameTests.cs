using Microsoft.Extensions.DependencyInjection;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests.Auth;

public class DisplayNameTests
{
    [Theory]
    [InlineData("业务人员", true)]
    [InlineData("系统管理员", true)]
    [InlineData("operator", true)]
    [InlineData("", true)]
    [InlineData("张三", false)]
    public void HasPlaceholderName_DetectsRoleLikeNames(string display, bool expected)
    {
        var u = new CurrentUser { Id = 1, Username = "u1", DisplayName = display, Role = Roles.Business };
        Assert.Equal(expected, u.HasPlaceholderName);
    }

    [Fact]
    public void DisplayName_EqualToUsername_IsPlaceholder()
        => Assert.True(new CurrentUser { Username = "zhangsan", DisplayName = "zhangsan" }.HasPlaceholderName);

    [Fact]
    public async Task UpdateDisplayName_PersistsAndRefreshesSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "mm_auth_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMaterialMgmtInfrastructure(root);
            await using var sp = services.BuildServiceProvider();
            await sp.GetRequiredService<DbBootstrap>().InitializeAsync();
            var auth = sp.GetRequiredService<IAuthService>();

            var me = auth.Login("operator", "operator123");
            Assert.NotNull(me);
            Assert.True(me!.HasPlaceholderName);

            var updated = auth.UpdateDisplayName(me.Id, "  张三 ");
            Assert.Equal("张三", updated.DisplayName);
            Assert.Equal("张三", auth.CurrentUser!.DisplayName);
            Assert.False(updated.HasPlaceholderName);
            Assert.Throws<InvalidOperationException>(() => auth.UpdateDisplayName(me.Id, "   "));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
