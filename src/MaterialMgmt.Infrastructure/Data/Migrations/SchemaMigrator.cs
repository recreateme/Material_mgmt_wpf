using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MaterialMgmt.Infrastructure.Data.Migrations;

/// <summary>迁移步骤可用的操作；所有语句都在该步骤的事务内执行。</summary>
public sealed class MigrationContext
{
    private readonly SqliteConnection _conn;
    private readonly SqliteTransaction _tx;
    private readonly CancellationToken _ct;

    internal MigrationContext(SqliteConnection conn, SqliteTransaction tx, CancellationToken ct)
    {
        _conn = conn;
        _tx = tx;
        _ct = ct;
    }

    public async Task ExecAsync(string sql)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(_ct);
    }

    public Task<HashSet<string>> ColumnsAsync(string table) => SchemaMigrator.ColumnsAsync(_conn, _tx, table, _ct);

    /// <summary>列不存在时添加；返回是否真的添加了。表名与列名来自代码常量，不接受外部输入。</summary>
    public async Task<bool> AddColumnAsync(string table, string column, string type)
    {
        if ((await ColumnsAsync(table)).Contains(column)) return false;
        await ExecAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        return true;
    }

    public async Task StampAlembicAsync(string revision)
    {
        await ExecAsync("CREATE TABLE IF NOT EXISTS alembic_version (version_num VARCHAR(32) NOT NULL PRIMARY KEY)");
        await ExecAsync("DELETE FROM alembic_version");
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = "INSERT INTO alembic_version (version_num) VALUES ($rev)";
        cmd.Parameters.AddWithValue("$rev", revision);
        await cmd.ExecuteNonQueryAsync(_ct);
    }
}

/// <summary>
/// 按 <c>PRAGMA user_version</c> 顺序执行未做过的迁移。每一步单独一个事务，
/// 与版本号一起提交：某一步失败只回滚该步，库停留在上一个完整版本。
/// </summary>
public static class SchemaMigrator
{
    public static async Task<int> GetVersionAsync(SqliteConnection conn, CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>直接把版本号设为最新：仅用于按当前模型新建的空库。</summary>
    public static async Task SetVersionAsync(SqliteConnection conn, int version, CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version}";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>执行版本号大于当前值的迁移，返回本次执行过的版本号。</summary>
    public static async Task<IReadOnlyList<int>> MigrateAsync(
        SqliteConnection conn,
        ILogger logger,
        IReadOnlyList<SchemaMigration>? migrations = null,
        CancellationToken ct = default)
    {
        migrations ??= SchemaMigrations.All;
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var current = await GetVersionAsync(conn, ct);
        var latest = migrations.Count == 0 ? 0 : migrations[^1].Version;
        if (current > latest)
        {
            logger.LogWarning("数据库结构版本 {Current} 高于本程序支持的 {Latest}（可能被新版程序打开过），按兼容模式继续", current, latest);
            return Array.Empty<int>();
        }

        var applied = new List<int>();
        foreach (var m in migrations.Where(x => x.Version > current).OrderBy(x => x.Version))
        {
            await using var tx = conn.BeginTransaction();
            await m.Apply(new MigrationContext(conn, tx, ct));
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"PRAGMA user_version = {m.Version}";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            applied.Add(m.Version);
            logger.LogInformation("数据库迁移 {Version} 完成：{Name}", m.Version, m.Name);
        }
        return applied;
    }

    internal static async Task<HashSet<string>> ColumnsAsync(
        SqliteConnection conn, SqliteTransaction? tx, string table, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetString(1));
        return set;
    }
}
