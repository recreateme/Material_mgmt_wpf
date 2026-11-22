namespace MaterialMgmt.Infrastructure.Data.Migrations;

/// <summary>
/// 结构升级。SchemaMigrations.All 按版本号顺序执行，版本写入 PRAGMA user_version。
/// 已发布的步骤不要改；新字段加在列表末尾。任一步失败由 DbBootstrap 用启动备份恢复整库。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "有版本号的结构升级步骤。";
}
