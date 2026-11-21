using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests;

public class DbSmokeTests
{
    [Fact]
    public async Task Can_Initialize_And_Read_Users()
    {
        // 用临时目录建库：不能碰仓库根下的 data/material_mgmt.db（那是在用的库）
        var root = Path.Combine(Path.GetTempPath(), "mm_smoke_" + Guid.NewGuid().ToString("N"));
        try
        {
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
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { /* 临时目录 */ }
        }
    }
}
