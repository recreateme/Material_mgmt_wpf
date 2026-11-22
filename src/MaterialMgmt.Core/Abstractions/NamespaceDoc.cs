namespace MaterialMgmt.Core.Abstractions;

/// <summary>
/// Core 需要、但必须由 Infrastructure 实现的接口：配置文件、登录、审计日志、程序根目录。
/// 这样 Core 不用引用 SQLite 或 INI 读写库。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "交给 Infrastructure 实现的接口。";
}
