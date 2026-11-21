namespace MaterialMgmt.Infrastructure.Data.Migrations;

/// <summary>一次结构升级。<see cref="Version"/> 写入 <c>PRAGMA user_version</c>，必须从 1 连续递增。</summary>
public sealed record SchemaMigration(int Version, string Name, Func<MigrationContext, Task> Apply);

/// <summary>
/// 有序迁移列表。v4.4 之前的库没有 user_version（为 0），各时期补过的列不尽相同，
/// 所以每一步都按“列不存在才添加”编写：任何时期的旧库都能从第 1 步顺序执行到最新。
/// 新增字段时在末尾追加一步，不要修改已发布的步骤。
/// </summary>
public static class SchemaMigrations
{
    /// <summary>与 Python 版 Alembic 链末端对应的修订号；Python 工具据此判断库已是最新结构。</summary>
    public const string AlembicHead = "0006";

    public static IReadOnlyList<SchemaMigration> All { get; } = new[]
    {
        new SchemaMigration(1, "baseline：工艺参数列（Alembic 0005/0006）与配方编码", async m =>
        {
            await m.AddColumnAsync("work_orders", "temperature", "REAL");
            await m.AddColumnAsync("work_orders", "humidity", "REAL");
            await m.AddColumnAsync("work_orders", "production_no", "VARCHAR(64)");
            await m.AddColumnAsync("work_orders", "d50", "REAL");
            await m.AddColumnAsync("work_orders", "mixing_exception", "BOOLEAN");
            await m.AddColumnAsync("work_orders", "base_film", "VARCHAR(128)");
            await m.AddColumnAsync("work_orders", "batch_no", "VARCHAR(64)");
            await m.AddColumnAsync("work_orders", "thickness", "REAL");
            await m.AddColumnAsync("work_orders", "coating_exception", "BOOLEAN");
            await m.AddColumnAsync("work_orders", "mixing_exception_note", "VARCHAR(512)");
            await m.AddColumnAsync("work_orders", "coating_exception_note", "VARCHAR(512)");

            if (await m.AddColumnAsync("formulas", "code", "VARCHAR(64)"))
            {
                await m.ExecAsync("UPDATE formulas SET code = 'FML-' || printf('%06d', id) WHERE code IS NULL");
                await m.ExecAsync("CREATE UNIQUE INDEX IF NOT EXISTS ix_formulas_code ON formulas (code)");
            }

            await m.StampAlembicAsync(AlembicHead);
        }),

        new SchemaMigration(2, "v4.2.1：工单软删除、称重记录快照与作废留痕", async m =>
        {
            await m.AddColumnAsync("work_orders", "deleted_at", "DATETIME");
            await m.AddColumnAsync("work_orders", "deleted_by", "VARCHAR(64)");

            await m.AddColumnAsync("weight_records", "weight_min_snapshot", "REAL");
            await m.AddColumnAsync("weight_records", "weight_max_snapshot", "REAL");
            await m.AddColumnAsync("weight_records", "material_code_snapshot", "VARCHAR(64)");
            await m.AddColumnAsync("weight_records", "material_name_snapshot", "VARCHAR(128)");
            await m.AddColumnAsync("weight_records", "voided_at", "DATETIME");
            await m.AddColumnAsync("weight_records", "voided_by", "VARCHAR(64)");

            // 快照列出现前的旧记录：取当前配方限值与原料回填（尽力而为，配方可能已改过）
            await m.ExecAsync(@"
UPDATE weight_records SET
  weight_min_snapshot = (SELECT fi.weight_min FROM formula_items fi WHERE fi.id = weight_records.formula_item_id),
  weight_max_snapshot = (SELECT fi.weight_max FROM formula_items fi WHERE fi.id = weight_records.formula_item_id),
  material_code_snapshot = (SELECT m.code FROM formula_items fi JOIN materials m ON m.id = fi.material_id WHERE fi.id = weight_records.formula_item_id),
  material_name_snapshot = (SELECT m.name FROM formula_items fi JOIN materials m ON m.id = fi.material_id WHERE fi.id = weight_records.formula_item_id)
WHERE weight_min_snapshot IS NULL AND weight_max_snapshot IS NULL");
        }),

        new SchemaMigration(3, "v4.2.3：作废原因与扫码来源", async m =>
        {
            await m.AddColumnAsync("weight_records", "void_reason", "VARCHAR(256)");
            await m.AddColumnAsync("weight_records", "scan_source", "VARCHAR(16)");
        }),
    };

    public static int Latest => All[^1].Version;
}
