namespace MaterialMgmt.Core.Orders;

/// <summary>
/// 工单规则与传给服务的参数：状态能否取消或删除、作废原因、开工单参数、列表筛选、今日统计。
/// 真正写入 work_orders 的是 Infrastructure 的 OrderService。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "工单能否取消、删除、筛选的纯规则，不写库。";
}
