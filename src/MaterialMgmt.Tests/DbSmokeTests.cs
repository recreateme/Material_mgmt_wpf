using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests;

public class DbSmokeTests
{
    [Fact]
    public async Task Can_Initialize_And_Read_Users()
    {
        // 从测试输出目录向上查找含 WPF重构计划.md 的仓库根
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sentinel = Path.Combine(root, "WPF重构计划.md");
        if (!File.Exists(sentinel))
            throw new InvalidOperationException($"未找到仓库根目录（缺少 {sentinel}）");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(root);
        await using var sp = services.BuildServiceProvider();
        var boot = sp.GetRequiredService<DbBootstrap>();
        await boot.InitializeAsync();

        var factory = sp.GetRequiredService<IDbContextFactory<MaterialMgmtDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var count = await db.Users.CountAsync();
        Assert.True(count >= 1);
        await boot.ShutdownAsync();
    }
}
