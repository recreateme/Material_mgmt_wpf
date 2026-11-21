using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Data.Migrations;

namespace MaterialMgmt.Tests.Data;

/// <summary>用三个时期的库结构（按真实旧库 .schema 还原）演练版本化迁移。</summary>
public sealed class SchemaMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm_migrate_" + Guid.NewGuid().ToString("N"));

    public SchemaMigrationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录，清理失败无碍 */ }
    }

    private string DbPath => Path.Combine(_root, "data", "material_mgmt.db");

    public enum Era { V30, V41, V422 }

    [Fact]
    public void Migration_versions_are_contiguous_from_one()
    {
        var versions = SchemaMigrations.All.Select(m => m.Version).ToList();
        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
    }

    [Theory]
    [InlineData(Era.V30)]
    [InlineData(Era.V41)]
    [InlineData(Era.V422)]
    public async Task Legacy_db_upgrades_to_latest_and_keeps_data(Era era)
    {
        CreateLegacyDb(era);

        await using var sp = BuildServices();
        await sp.GetRequiredService<DbBootstrap>().InitializeAsync();

        await using var conn = Open();
        Assert.Equal(SchemaMigrations.Latest, await SchemaMigrator.GetVersionAsync(conn));
        Assert.Equal(SchemaMigrations.AlembicHead, Scalar(conn, "SELECT version_num FROM alembic_version"));

        await using var db = await sp.GetRequiredService<IDbContextFactory<MaterialMgmtDbContext>>().CreateDbContextAsync();
        Assert.Equal(2, await db.Materials.CountAsync());
        Assert.Equal(2, await db.FormulaItems.CountAsync());
        Assert.Equal(1, await db.WorkOrders.CountAsync());
        var records = await db.WeightRecords.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(2, records.Count);
        // 快照列出现之前的记录按当前配方回填；v4.2.2 的记录本来就有快照，保持原值
        Assert.All(records, r => Assert.NotNull(r.WeightMinSnapshot));
        Assert.Equal(era == Era.V422 ? "MAT-OLD" : "MAT-000001", records[0].MaterialCodeSnapshot);
        Assert.All(records, r => Assert.Null(r.ScanSource));

        var users = await db.Users.AsNoTracking().ToListAsync();
        Assert.All(users, u => Assert.True(u.Role is Roles.SystemAdmin or Roles.Business));
    }

    [Fact]
    public async Task Second_startup_runs_no_migration()
    {
        CreateLegacyDb(Era.V41);
        await using (var sp = BuildServices())
            await sp.GetRequiredService<DbBootstrap>().InitializeAsync();

        await using var conn = Open();
        var applied = await SchemaMigrator.MigrateAsync(conn, NullLogger.Instance);
        Assert.Empty(applied);

        await using var sp2 = BuildServices();
        await sp2.GetRequiredService<DbBootstrap>().InitializeAsync();
        Assert.Equal(SchemaMigrations.Latest, await SchemaMigrator.GetVersionAsync(conn));
    }

    [Fact]
    public async Task Empty_db_is_created_at_latest_version()
    {
        await using var sp = BuildServices();
        await sp.GetRequiredService<DbBootstrap>().InitializeAsync();

        await using var conn = Open();
        Assert.Equal(SchemaMigrations.Latest, await SchemaMigrator.GetVersionAsync(conn));
        Assert.Equal(SchemaMigrations.AlembicHead, Scalar(conn, "SELECT version_num FROM alembic_version"));
        Assert.Equal(2L, Scalar(conn, "SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Failed_migration_restores_startup_backup()
    {
        CreateLegacyDb(Era.V41);
        await using var sp = BuildServices();
        var boot = sp.GetRequiredService<DbBootstrap>();
        var broken = SchemaMigrations.Latest + 1;
        boot.Migrations = SchemaMigrations.All
            .Append(new SchemaMigration(broken, "故意失败", async m =>
            {
                await m.AddColumnAsync("work_orders", "will_be_rolled_back", "TEXT");
                throw new InvalidOperationException("模拟迁移失败");
            }))
            .ToList();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.InitializeAsync());
        Assert.Contains("已自动恢复", ex.Message);

        SqliteConnection.ClearAllPools();
        await using var conn = Open();
        // 回到启动前：没有任何迁移痕迹，旧数据完整
        Assert.Equal(0, await SchemaMigrator.GetVersionAsync(conn));
        var cols = Columns(conn, "work_orders");
        Assert.DoesNotContain("deleted_at", cols);
        Assert.DoesNotContain("will_be_rolled_back", cols);
        Assert.Equal(2L, Scalar(conn, "SELECT COUNT(*) FROM weight_records"));
    }

    [Fact]
    public async Task Failed_step_rolls_back_only_itself()
    {
        CreateLegacyDb(Era.V41);
        await using var conn = Open();
        var steps = new[]
        {
            new SchemaMigration(1, "ok", m => m.AddColumnAsync("work_orders", "step_one", "TEXT")),
            new SchemaMigration(2, "fail", async m =>
            {
                await m.AddColumnAsync("work_orders", "step_two", "TEXT");
                throw new InvalidOperationException("boom");
            }),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaMigrator.MigrateAsync(conn, NullLogger.Instance, steps));

        Assert.Equal(1, await SchemaMigrator.GetVersionAsync(conn));
        var cols = Columns(conn, "work_orders");
        Assert.Contains("step_one", cols);
        Assert.DoesNotContain("step_two", cols);
    }

    [Fact]
    public async Task Newer_db_version_is_left_untouched()
    {
        CreateLegacyDb(Era.V41);
        await using var conn = Open();
        await SchemaMigrator.SetVersionAsync(conn, SchemaMigrations.Latest + 5);

        var applied = await SchemaMigrator.MigrateAsync(conn, NullLogger.Instance);

        Assert.Empty(applied);
        Assert.Equal(SchemaMigrations.Latest + 5, await SchemaMigrator.GetVersionAsync(conn));
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaterialMgmtInfrastructure(_root);
        return services.BuildServiceProvider();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        conn.Open();
        return conn;
    }

    private static object? Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static HashSet<string> Columns(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(1));
        return set;
    }

    private void CreateLegacyDb(Era era)
    {
        var processCols = era == Era.V30 ? "" :
            "temperature FLOAT, humidity FLOAT, production_no VARCHAR(64), d50 FLOAT, mixing_exception BOOLEAN, " +
            "base_film VARCHAR(128), batch_no VARCHAR(64), thickness FLOAT, coating_exception BOOLEAN, " +
            "mixing_exception_note VARCHAR(512), coating_exception_note VARCHAR(512), ";
        var softDeleteCols = era == Era.V422 ? "deleted_at DATETIME, deleted_by VARCHAR(64), " : "";
        var recordCols = era == Era.V422
            ? "weight_min_snapshot REAL, weight_max_snapshot REAL, material_code_snapshot VARCHAR(64), " +
              "material_name_snapshot VARCHAR(128), voided_at DATETIME, voided_by VARCHAR(64), "
            : "";

        var sql = $@"
CREATE TABLE alembic_version (version_num VARCHAR(32) NOT NULL, CONSTRAINT alembic_version_pkc PRIMARY KEY (version_num));
INSERT INTO alembic_version VALUES ('{(era == Era.V30 ? "0004" : "0006")}');
CREATE TABLE users (id INTEGER NOT NULL, username VARCHAR(64) NOT NULL, password_hash VARCHAR(256) NOT NULL,
  display_name VARCHAR(64) NOT NULL, role VARCHAR(16) NOT NULL, is_active BOOLEAN DEFAULT '1' NOT NULL,
  created_at DATETIME NOT NULL, updated_at DATETIME, PRIMARY KEY (id));
CREATE UNIQUE INDEX ix_users_username ON users (username);
CREATE TABLE materials (id INTEGER NOT NULL, code VARCHAR(64) NOT NULL, name VARCHAR(128) NOT NULL,
  description VARCHAR(512), created_at DATETIME NOT NULL, PRIMARY KEY (id));
CREATE UNIQUE INDEX ix_materials_code ON materials (code);
CREATE TABLE formulas (id INTEGER NOT NULL, name VARCHAR(128) NOT NULL, description VARCHAR(512), created_by VARCHAR(64),
  created_at DATETIME NOT NULL, is_active BOOLEAN DEFAULT '1' NOT NULL, code VARCHAR(64) NOT NULL, PRIMARY KEY (id));
CREATE UNIQUE INDEX ix_formulas_code ON formulas (code);
CREATE TABLE formula_items (id INTEGER NOT NULL, formula_id INTEGER NOT NULL, material_id INTEGER NOT NULL,
  sequence INTEGER NOT NULL, weight_min FLOAT NOT NULL, weight_max FLOAT NOT NULL, notes VARCHAR(256), PRIMARY KEY (id),
  UNIQUE (formula_id, sequence), FOREIGN KEY(formula_id) REFERENCES formulas (id) ON DELETE CASCADE,
  FOREIGN KEY(material_id) REFERENCES materials (id));
CREATE TABLE formula_erp_bindings (id INTEGER NOT NULL, formula_id INTEGER NOT NULL, erp_type_code VARCHAR(64) NOT NULL,
  erp_label VARCHAR(128), created_at DATETIME NOT NULL, PRIMARY KEY (id), FOREIGN KEY(formula_id) REFERENCES formulas (id));
CREATE UNIQUE INDEX ix_erp_code ON formula_erp_bindings (erp_type_code);
CREATE TABLE work_orders (id INTEGER NOT NULL, formula_id INTEGER NOT NULL, operator_id INTEGER, operator VARCHAR(64) NOT NULL,
  erp_type_code VARCHAR(64), external_order_no VARCHAR(64), start_time DATETIME NOT NULL, end_time DATETIME,
  status VARCHAR(32) DEFAULT 'in_progress' NOT NULL, {processCols}{softDeleteCols}
  PRIMARY KEY (id), FOREIGN KEY(formula_id) REFERENCES formulas (id), FOREIGN KEY(operator_id) REFERENCES users (id));
CREATE INDEX ix_wo_external ON work_orders (external_order_no);
CREATE TABLE weight_records (id INTEGER NOT NULL, work_order_id INTEGER NOT NULL, formula_item_id INTEGER NOT NULL,
  barcode_scanned VARCHAR(128) NOT NULL, tare_weight FLOAT, actual_weight FLOAT NOT NULL, timestamp DATETIME NOT NULL,
  is_valid BOOLEAN DEFAULT '1' NOT NULL, note VARCHAR(256), {recordCols}
  PRIMARY KEY (id), FOREIGN KEY(work_order_id) REFERENCES work_orders (id), FOREIGN KEY(formula_item_id) REFERENCES formula_items (id));

INSERT INTO users VALUES (1, 'admin', 'x', '管理人员', 'admin', 1, '2025-01-01 08:00:00', NULL);
INSERT INTO users VALUES (2, 'operator', 'x', '操作员', 'operator', 1, '2025-01-01 08:00:00', NULL);
INSERT INTO materials VALUES (1, 'MAT-000001', '原料一', NULL, '2025-01-01 08:00:00');
INSERT INTO materials VALUES (2, 'MAT-000002', '原料二', NULL, '2025-01-01 08:00:00');
INSERT INTO formulas VALUES (1, '配方一', NULL, 'admin', '2025-01-01 08:00:00', 1, 'FML-000001');
INSERT INTO formula_items VALUES (1, 1, 1, 1, 10.0, 12.0, NULL);
INSERT INTO formula_items VALUES (2, 1, 2, 2, 5.0, 6.0, NULL);
INSERT INTO work_orders (id, formula_id, operator_id, operator, start_time, end_time, status)
  VALUES (1, 1, 2, 'operator', '2025-01-02 09:00:00', '2025-01-02 09:30:00', 'completed');
INSERT INTO weight_records (id, work_order_id, formula_item_id, barcode_scanned, actual_weight, timestamp, is_valid)
  VALUES (1, 1, 1, 'MAT-000001', 11.0, '2025-01-02 09:10:00', 1);
INSERT INTO weight_records (id, work_order_id, formula_item_id, barcode_scanned, actual_weight, timestamp, is_valid)
  VALUES (2, 1, 2, 'MAT-000002', 5.5, '2025-01-02 09:20:00', 1);
";
        if (era == Era.V422)
            sql += "UPDATE weight_records SET weight_min_snapshot = 9.0, weight_max_snapshot = 13.0, material_code_snapshot = 'MAT-OLD' WHERE id = 1;" +
                   "UPDATE weight_records SET weight_min_snapshot = 5.0, weight_max_snapshot = 6.0, material_code_snapshot = 'MAT-000002' WHERE id = 2;";

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
