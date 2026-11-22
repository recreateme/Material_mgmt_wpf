namespace MaterialMgmt.Infrastructure;

/// <summary>
/// 落地的一层：SQLite、串口、Excel、settings.ini，以及会 SaveChanges 的服务。
/// 可以引用 Core，不能引用 App（不能弹出窗口）。
/// 启动时由 ServiceCollectionExtensions 注册为单例。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "数据库、串口、导出、配置。界面通过这里的服务写库。";
}
