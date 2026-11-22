namespace MaterialMgmt.Infrastructure.Services;

/// <summary>
/// 业务写入的入口。面板只调用这些类，不直接 SaveChanges。
/// 规则不满足时抛 InvalidOperationException，消息给操作员看；编码问题抛 CodeError。
/// 审计日志不在服务里写，由界面在调用成功之后写。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "原料、配方、账号、工单、登录、配置的服务。";
}
