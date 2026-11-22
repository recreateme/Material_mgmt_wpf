namespace MaterialMgmt.Core.Workflow;

/// <summary>
/// 称量状态机。界面把扫码和克重送进来，这里只回答「当前步能不能确认、下一句提示是什么」。
/// 确认后的落库不在这里，由 OrderService.SavePendingWeight 完成。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "扫码与克重的状态判断，不写数据库。";
}
