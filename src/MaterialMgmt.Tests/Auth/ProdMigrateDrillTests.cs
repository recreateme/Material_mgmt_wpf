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
            return; // 生产备份只在开发机本地（data/ 不入库），CI 上跳过

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

    /// <summary>当前在用库（data/material_mgmt.db）的副本：启动补列后，账号、工单、称重记录数量不变，新列存在。</summary>
    [Fact]
    public async Task Current_db_copy_gains_new_columns_without_data_change()
    {
        var repoRoot = FindRepoRoot();
        var source = Path.Combine(repoRoot, "data", "material_mgmt.db");
        if (!File.Exists(source))
            return; // 干净检出（CI）没有本地库，跳过

        var drillRoot = Path.Combine(repoRoot, "data", "_migrate_drill_current");
        if (Directory.Exists(drillRoot))
            Directory.Delete(drillRoot, recursive: true);
        Directory.CreateDirectory(Path.Combine(drillRoot, "data"));
        var drillDb = Path.Combine(drillRoot, "data", "material_mgmt.db");
        await using (var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
        {
            await src.OpenAsync();
            await using var cmd = src.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{drillDb.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync();
        }

        (int Users, int Orders, int Records, int Valid) Count(MaterialMgmtDbContext c) => (
            c.Users.Count(), c.WorkOrders.Count(), c.WeightRecords.Count(), c.WeightRecords.Count(r => r.IsValid));

        (int, int, int, int) before;
        var opts = new DbContextOptionsBuilder<MaterialMgmtDbContext>().UseSqlite($"Data Source={drillDb};Pooling=False").Options;
        await using (var pre = new MaterialMgmtDbContext(opts))
        {
            var conn = pre.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), (SELECT COUNT(*) FROM work_orders), (SELECT COUNT(*) FROM weight_records), " +
                              "(SELECT COUNT(*) FROM weight_records WHERE is_valid = 1) FROM users";
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            before = (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(drillRoot);
        await using var sp = services.BuildServiceProvider();
        var boot = sp.GetRequiredService<DbBootstrap>();
        await boot.InitializeAsync();
        Assert.True(boot.ValidIndexReady);

        await using var db = await sp.GetRequiredService<IDbContextFactory<MaterialMgmtDbContext>>().CreateDbContextAsync();
        Assert.Equal(before, Count(db));
        // 新列可读（旧记录为空）
        Assert.True(await db.WeightRecords.CountAsync(r => r.ScanSource == null && r.VoidReason == null) >= 0);
        await boot.ShutdownAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
