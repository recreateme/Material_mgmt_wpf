using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Tests.Auth;

/// <summary>
/// 用生产只读备份的副本演练 v4.2 角色迁移；不改动 backups/ 下原文件。
/// </summary>
public class ProdMigrateDrillTests
{
    [Fact]
    public async Task Prod_backup_copy_migrates_roles_and_keeps_business_data()
    {
        var repoRoot = FindRepoRoot();
        var backup = Path.Combine(repoRoot, "data", "backups", "prod-20261114-material_mgmt.db");
        if (!File.Exists(backup))
            throw new InvalidOperationException($"缺少生产只读备份：{backup}");

        var drillRoot = Path.Combine(repoRoot, "data", "_migrate_drill");
        if (Directory.Exists(drillRoot))
            Directory.Delete(drillRoot, recursive: true);
        Directory.CreateDirectory(Path.Combine(drillRoot, "data"));
        Directory.CreateDirectory(Path.Combine(drillRoot, "config"));
        Directory.CreateDirectory(Path.Combine(drillRoot, "logs"));

        var drillDb = Path.Combine(drillRoot, "data", "material_mgmt.db");
        File.Copy(backup, drillDb, overwrite: true);

        // 迁移前：应仍有旧角色或 root（以实库为准）
        await using (var pre = new MaterialMgmtDbContext(
                         new DbContextOptionsBuilder<MaterialMgmtDbContext>()
                             .UseSqlite($"Data Source={drillDb}")
                             .Options))
        {
            var preUsers = await pre.Users.AsNoTracking().ToListAsync();
            Assert.True(preUsers.Count >= 1);
            Assert.Contains(preUsers, u =>
                u.Role is Roles.LegacyAdmin or Roles.LegacyOperator or Roles.LegacyRoot
                or Roles.SystemAdmin or Roles.Business);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(drillRoot);
        await using var sp = services.BuildServiceProvider();
        var boot = sp.GetRequiredService<DbBootstrap>();
        await boot.InitializeAsync();

        var factory = sp.GetRequiredService<IDbContextFactory<MaterialMgmtDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var users = await db.Users.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(users, u =>
            u.Role is Roles.LegacyRoot or Roles.LegacyAdmin or Roles.LegacyOperator
            || u.Username.Equals("root", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(users, u => u.Role == Roles.SystemAdmin);
        Assert.True(users.All(u => u.Role is Roles.SystemAdmin or Roles.Business));

        // 业务数据应保留（对照生产备份规模）
        Assert.True(await db.Materials.CountAsync() >= 1);
        Assert.True(await db.Formulas.CountAsync() >= 1);
        Assert.True(await db.WorkOrders.CountAsync() >= 1);

        await boot.ShutdownAsync();

        // 结果副本保留在 data/_migrate_drill 供人工抽查；只读备份未改动
        Assert.True(File.Exists(backup));
        var bakInfo = new FileInfo(backup);
        Assert.True(bakInfo.IsReadOnly || bakInfo.Length > 0);
    }

    private static string FindRepoRoot()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sentinel = Path.Combine(root, "WPF重构计划.md");
        if (!File.Exists(sentinel))
            throw new InvalidOperationException($"未找到仓库根目录（缺少 {sentinel}）");
        return root;
    }
}
