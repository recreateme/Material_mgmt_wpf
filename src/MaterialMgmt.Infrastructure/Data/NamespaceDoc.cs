namespace MaterialMgmt.Infrastructure.Data;

/// <summary>
/// EF Core 上下文和启动时的数据库准备（DbBootstrap）：备份、空库建表、旧库迁移、种子账号。
/// 正在使用的库是根目录 data/material_mgmt.db。测试不得打开这份文件。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "数据库上下文和启动初始化。";
}
