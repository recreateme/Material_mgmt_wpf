using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;

namespace MaterialMgmt.Infrastructure.Data;

/// <summary>启动初始化：建表（空库）、Alembic 兼容校验、种子账号、WAL。</summary>
public sealed class DbBootstrap
{
    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private readonly IAuthService _auth;
    private readonly ILogger<DbBootstrap> _logger;

    public DbBootstrap(
        IAppPaths paths,
        IDbContextFactory<MaterialMgmtDbContext> factory,
        IAuthService auth,
        ILogger<DbBootstrap> logger)
    {
        _paths = paths;
        _factory = factory;
        _auth = auth;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.DataDir);
        Directory.CreateDirectory(_paths.ConfigDir);
        Directory.CreateDirectory(_paths.LogsDir);

        await using var db = await _factory.CreateDbContextAsync(ct);
        await EnsurePragmasAsync(db, ct);
        await EnsureSchemaAsync(db, ct);
        await SeedDefaultsAsync(db, ct);
        _logger.LogInformation("数据库初始化完成：{Path}", _paths.DatabasePath);
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
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);
    }

    private async Task EnsureSchemaAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
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
            _logger.LogInformation("空库或缺少业务表，使用 EnsureCreated 建表");
            await db.Database.EnsureCreatedAsync(ct);
            await StampAlembicAsync(db, "0006", ct);
            return;
        }

        // 兼容旧库：无 alembic_version 时按列推断并 stamp
        if (!tables.Contains("alembic_version"))
        {
            var stamp = await InferStampAsync(db, ct);
            _logger.LogInformation("检测到无 alembic_version 的旧库，标记为 {Rev}", stamp);
            await StampAlembicAsync(db, stamp, ct);
        }

        // 确保关键工艺参数列存在（增量补齐，避免现场缺列）
        await EnsureWorkOrderColumnsAsync(db, ct);
    }

    private static async Task<string> InferStampAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var cols = await GetColumnsAsync(db, "work_orders", ct);
        if (cols.Contains("coating_exception_note")) return "0006";
        if (cols.Contains("temperature")) return "0005";
        return "0004";
    }

    private static async Task EnsureWorkOrderColumnsAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var cols = await GetColumnsAsync(db, "work_orders", ct);
        var alters = new List<string>();
        void Need(string col, string ddl)
        {
            if (!cols.Contains(col)) alters.Add($"ALTER TABLE work_orders ADD COLUMN {ddl}");
        }

        Need("temperature", "temperature REAL");
        Need("humidity", "humidity REAL");
        Need("production_no", "production_no VARCHAR(64)");
        Need("d50", "d50 REAL");
        Need("mixing_exception", "mixing_exception BOOLEAN");
        Need("base_film", "base_film VARCHAR(128)");
        Need("batch_no", "batch_no VARCHAR(64)");
        Need("thickness", "thickness REAL");
        Need("coating_exception", "coating_exception BOOLEAN");
        Need("mixing_exception_note", "mixing_exception_note VARCHAR(512)");
        Need("coating_exception_note", "coating_exception_note VARCHAR(512)");

        foreach (var sql in alters)
            await db.Database.ExecuteSqlRawAsync(sql, ct);

        // formulas.code 若缺失（极旧库）
        var fcols = await GetColumnsAsync(db, "formulas", ct);
        if (!fcols.Contains("code"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE formulas ADD COLUMN code VARCHAR(64)", ct);
    }

    private static async Task StampAlembicAsync(MaterialMgmtDbContext db, string rev, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS alembic_version (version_num VARCHAR(32) NOT NULL PRIMARY KEY)", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM alembic_version", ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO alembic_version (version_num) VALUES ({0})", rev, ct);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        MaterialMgmtDbContext db, string table, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetString(1));
        return set;
    }

    private async Task SeedDefaultsAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var count = await db.Users.CountAsync(ct);
        if (count == 0)
        {
            var seeds = new (string U, string Pw, string Display, string Role)[]
            {
                ("root", "root666", "开发人员", Roles.Root),
                ("admin", "88888888", "李园涛", Roles.Admin),
                ("operator", "operator123", "实验人员", Roles.Operator),
                ("王瑶", "88888888", "王瑶", Roles.Operator),
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

        // 非首次：仅同步已存在业务账号的角色/显示名
        var business = new (string U, string Display, string Role)[]
        {
            ("admin", "李园涛", Roles.Admin),
            ("王瑶", "王瑶", Roles.Operator),
        };
        foreach (var (u, display, role) in business)
        {
            var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u, ct);
            if (user is null) continue;
            var changed = false;
            if (user.Role != role) { user.Role = role; changed = true; }
            if (user.DisplayName != display) { user.DisplayName = display; changed = true; }
            if (changed) _logger.LogInformation("业务账号已同步：{User}", u);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>将历史账号「李园涛」合并为 admin（密码保持 88888888 不变）。</summary>
    private async Task MigrateLegacyAdminAccountAsync(MaterialMgmtDbContext db, CancellationToken ct)
    {
        var legacy = await db.Users.FirstOrDefaultAsync(x => x.Username == "李园涛", ct);
        if (legacy is null) return;

        var admin = await db.Users.FirstOrDefaultAsync(x => x.Username == "admin", ct);
        if (admin is not null && admin.Id != legacy.Id)
            db.Users.Remove(admin);

        legacy.Username = "admin";
        if (string.IsNullOrWhiteSpace(legacy.DisplayName) || legacy.DisplayName == "李园涛")
            legacy.DisplayName = "李园涛";
        legacy.Role = Roles.Admin;
        legacy.IsActive = true;
        _logger.LogInformation("已将账号「李园涛」重命名为 admin（密码未改）");
        await db.SaveChangesAsync(ct);
    }
}
