using MaterialMgmt.Core.Entities;

namespace MaterialMgmt.Core.Orders;

/// <summary>
/// 工单状态文字和两条硬规则：已完成不能取消；进行中必须先取消才能删除。
/// 改状态字段由调用方保存，本类不访问数据库。
/// </summary>
public static class OrderRules
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static void Cancel(WorkOrder wo)
    {
        if (wo.Status == Completed)
            throw new InvalidOperationException("已完成的工单不可取消");
        wo.Status = Cancelled;
        wo.EndTime = DateTime.Now;
    }

    public static void EnsureCanDelete(WorkOrder wo, bool allowCompleted)
    {
        if (wo.Status == InProgress)
            throw new InvalidOperationException("进行中的工单请先取消再删除");
        if (wo.Status == Completed && !allowCompleted)
            throw new InvalidOperationException("已完成的工单不可删除");
    }

    public static string ToDisplay(string status) => status switch
    {
        Completed => "已完成",
        Cancelled => "已取消",
        InProgress => "进行中",
        _ => status
    };
}
