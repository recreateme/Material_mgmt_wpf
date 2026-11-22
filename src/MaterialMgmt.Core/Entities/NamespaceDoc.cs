namespace MaterialMgmt.Core.Entities;

/// <summary>
/// 与数据库表一一对应的类。改字段时要同时改 MaterialMgmtDbContext，并在 SchemaMigrations 末尾追加一步。
/// 类本身不包含业务规则。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "表对应的实体：原料、配方、步骤、工单、称重记录、账号。";
}
