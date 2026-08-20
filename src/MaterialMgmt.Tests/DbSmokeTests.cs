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
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        // Prefer repo root containing data/
        if (!Directory.Exists(Path.Combine(root, "data")))
            root = @"D:\Develop\Material_Mgmt";

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
