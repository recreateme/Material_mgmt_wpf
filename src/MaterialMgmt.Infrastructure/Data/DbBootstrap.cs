using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data.Migrations;

namespace MaterialMgmt.Infrastructure.Data;

/// <summary>启动初始化：备份、建表（空库）或按版本迁移（旧库）、有效记录索引、种子账号与角色迁移、WAL。</summary>
public sealed class DbBootstrap
{
    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private readonly IAuthService _auth;
    private readonly IConfigService _config;
    private readonly ILogger<DbBootstrap> _logger;

    public DbBootstrap(
        IAppPaths paths,
        IDbContextFactory<MaterialMgmtDbContext> factory,
        IAuthService auth,
        IConfigService config,
        ILogger<DbBootstrap> logger)
    {
        _paths = paths;
        _factory = factory;
        _auth = auth;
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.DataDir);
        Directory.CreateDirectory(_paths.ConfigDir);
        Directory.CreateDirectory(_paths.LogsDir);

        // 任何迁移 / 种子写入之前先备份，迁移失败时可回滚到启动前状态
        var startupBackup = await BackupAsync("startup", ct);

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            await EnsurePragmasAsync(db, ct);
            try
            {
                await EnsureSchemaAsync(db, ct);
            }
            catch (Exception ex) when (startupBackup is not null)
            {
                await db.DisposeAsync();
                RestoreFrom(startupBackup);
                throw new InvalidOperationException(
                    $"数据库升级失败，已自动恢复为启动前的备份，数据未受影响。\n原因：{ex.GetBaseException().Message}", ex);
            }
        }

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            await TryCreateValidIndexAsync(db, ct);
            await SeedDefaultsAsync(db, ct);
        }
        _logger.LogInformation("数据库初始化完成：{Path}", _paths.DatabasePath);
    }

    /// <summary>结构迁移步骤；测试可替换以模拟失败。</summary>
    internal IReadOnlyList<SchemaMigration> Migrations { get; set; } = SchemaMigrations.All;

    /// <summary>用备份覆盖当前库。调用前必须释放所有指向该库的连接。</summary>
    private void RestoreFrom(string backup)
    {
        SqliteConnection.ClearAllPools();
        var db = _paths.DatabasePath;
        foreach (var side in new[] { db + "-wal", db + "-shm" })
        {
            if (File.Exists(side)) File.Delete(side);
        }
        File.Copy(backup, db, overwrite: true);
        _logger.LogError("数据库升级失败，已从 {Backup} 恢复", backup);
    }

    /// <summary>每种备份原因（startup / exit / manual）各自保留的份数。</summary>
    private const int BackupKeep = 14;

    /// <summary>启动时“有效记录唯一索引”是否建成；false 表示旧数据存在重复有效记录，需管理员修复。</summary>
    public bool ValidIndexReady { get; private set; } = true;

    public string BackupDir => Path.Combine(_paths.DataDir, "backup");

    /// <summary>最近一次备份文件的写入时间；没有备份时为 null。</summary>
    public DateTime? LatestBackupTime()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return null;
            var latest = new DirectoryInfo(BackupDir).GetFiles("material_mgmt_*.db")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            return latest?.LastWriteTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 用 SQLite VACUUM INTO 生成一致性快照到 data/backup/。失败只记日志，不阻断启动。
    /// 返回备份文件路径；库不存在 / 为空 / 失败时返回 null。
    /// </summary>
    public async Task<string?> BackupAsync(string reason, CancellationToken ct = default)
    {
        try
        {
            var src = _paths.DatabasePath;
            if (!File.Exists(src) || new FileInfo(src).Length == 0) return null;

            var dir = BackupDir;
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"material_mgmt_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.db");
            if (File.Exists(target)) return null;

            await using (var conn = new SqliteConnection($"Data Source={src};Pooling=False"))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                // 路径中的单引号需转义；VACUUM INTO 不支持参数绑定
                cmd.CommandText = $"VACUUM INTO '{target.Replace("'", "''")}'";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            _logger.LogInformation("数据库已备份：{Path}", target);

            var old = new DirectoryInfo(dir).GetFiles($"material_mgmt_*_{reason}.db")
                .OrderByDescending(f => f.Name)
                .Skip(BackupKeep);
            foreach (var f in old)
            {
                try { f.Delete(); } catch { /* 忽略清理失败 */ }
            }
            return target;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "数据库备份失败（已忽略，不影响启动）");
            return null;
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_paths.DatabasePath}");
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("WAL checkpoint 完成");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WAL checkpoint 失败");
        }
    }

    private static async Task EnsurePragmasAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken: ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken: ct);
    }

    private async Task EnsureSchemaAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var conn = (SqliteConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                tables.Add(reader.GetString(0));
        }

        if (tables.Count == 0 || !tables.Contains("users"))
        {
            _logger.LogInformation("空库或缺少业务表，按当前模型建表（结构版本 {Version}）", SchemaMigrations.Latest);
            await db.Database.EnsureCreatedAsync(ct);
            await using (var tx = conn.BeginTransaction())
            {
                await new MigrationContext(conn, tx, ct).StampAlembicAsync(SchemaMigrations.AlembicHead);
                await tx.CommitAsync(ct);
            }
            await SchemaMigrator.SetVersionAsync(conn, SchemaMigrations.Latest, ct);
            return;
        }

        var from = await SchemaMigrator.GetVersionAsync(conn, ct);
        var applied = await SchemaMigrator.MigrateAsync(conn, _logger, Migrations, ct);
        if (applied.Count > 0)
            _logger.LogInformation("数据库结构已从版本 {From} 升级到 {To}", from, applied[^1]);
    }

    /// <summary>
    /// “同工单同步骤至多一条有效记录”的部分唯一索引。每次启动都尝试，而不放进迁移：
    /// 旧数据存在重复有效记录时会失败，只记警告、不阻断启动，等系统管理员在首页修复后再建。
    /// </summary>

    /// <summary>重复有效记录修复后由界面调用，重新创建唯一索引。</summary>
    public async Task<bool> TryCreateValidIndexAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await TryCreateValidIndexAsync(db, ct);
    }

    private async Task<bool> TryCreateValidIndexAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_weight_records_valid " +
                "ON weight_records(work_order_id, formula_item_id) WHERE is_valid = 1",
                cancellationToken: ct);
            ValidIndexReady = true;
        }
        catch (Exception ex)
        {
            ValidIndexReady = false;
            _logger.LogWarning(ex, "创建 ux_weight_records_valid 失败：旧数据存在同一步骤的重复有效记录，等待系统管理员在首页修复");
        }
        return ValidIndexReady;
    }

    private async Task SeedDefaultsAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var count = await db.Users.CountAsync(ct);
        if (count == 0)
        {
            var seeds = new (string U, string Pw, string Display, string Role)[]
            {
                ("admin", PasswordPolicy.SeedPasswords["admin"], "系统管理员", Roles.SystemAdmin),
                ("operator", PasswordPolicy.SeedPasswords["operator"], "业务人员", Roles.Business),
            };
            foreach (var (u, pw, display, role) in seeds)
            {
                db.Users.Add(new User
                {
                    Username = u,
                    PasswordHash = _auth.HashPassword(pw),
                    DisplayName = display,
                    Role = role,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
                _logger.LogInformation("种子账号已创建：{User} ({Role})", u, role);
            }
            await db.SaveChangesAsync(ct);
            return;
        }

        await MigrateLegacyAdminAccountAsync(db, ct);
        await MigrateRolesV42Async(db, ct);
    }

    /// <summary>v4.2：admin→system_admin，operator→business，删除 root。不改动业务表数据。</summary>
    private async Task MigrateRolesV42Async(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var snaps = await db.Users
            .Select(u => new RoleMigration.UserSnap(u.Id, u.Username, u.Role))
            .ToListAsync(ct);

        // 已全部为新角色且无 root → 跳过
        if (snaps.All(s => s.Role is Roles.SystemAdmin or Roles.Business))
            return;

        var plan = RoleMigration.Plan(snaps);
        if (plan.Error is not null)
        {
            _logger.LogError("角色迁移中止：{Error}", plan.Error);
            throw new InvalidOperationException($"角色迁移失败：{plan.Error}");
        }

        foreach (var (id, newRole) in plan.Updates)
        {
            var u = await db.Users.FirstAsync(x => x.Id == id, ct);
            var old = u.Role;
            u.Role = newRole;
            if (newRole == Roles.SystemAdmin &&
                (u.DisplayName is "管理人员" or "开发人员" || IsLegacyAdminName(u.DisplayName)))
                u.DisplayName = "系统管理员";
            _logger.LogInformation("角色已迁移：{User} {Old} → {New}", u.Username, old, newRole);
        }

        foreach (var id in plan.DeleteIds)
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) continue;
            db.Users.Remove(u);
            _logger.LogInformation("已删除废弃账号：{User}", u.Username);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 旧版曾用个人账号充当管理员。在 settings.ini 的 [migration] legacy_admin_username 写上该用户名，
    /// 启动时会把它合并为 admin（密码保持不变）；未配置时不做任何事。
    /// </summary>
    private string LegacyAdminUsername => _config.GetValue("migration", "legacy_admin_username", "").Trim();

    private bool IsLegacyAdminName(string? name)
        => LegacyAdminUsername.Length > 0 && string.Equals(name, LegacyAdminUsername, StringComparison.Ordinal);

    private async Task MigrateLegacyAdminAccountAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var legacyName = LegacyAdminUsername;
        if (legacyName.Length == 0 || legacyName == "admin") return;
        var legacy = await db.Users.FirstOrDefaultAsync(x => x.Username == legacyName, ct);
        if (legacy is null) return;

        var admin = await db.Users.FirstOrDefaultAsync(x => x.Username == "admin", ct);
        if (admin is not null && admin.Id != legacy.Id)
            db.Users.Remove(admin);

        legacy.Username = "admin";
        if (string.IsNullOrWhiteSpace(legacy.DisplayName) ||
            legacy.DisplayName is "管理人员" or "系统管理员" || legacy.DisplayName == legacyName)
            legacy.DisplayName = "系统管理员";
        legacy.Role = Roles.LegacyAdmin; // 随后由 MigrateRolesV42Async 转为 system_admin
        legacy.IsActive = true;
        _logger.LogInformation("已将历史管理员账号「{User}」重命名为 admin（密码未改）", legacyName);
        await db.SaveChangesAsync(ct);
    }
}
