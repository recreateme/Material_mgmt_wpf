namespace MaterialMgmt.App.Views.Panels;

/// <summary>
/// 侧栏每一个名字对应的页面。页面负责摆控件、调用服务、成功后写审计日志。
/// 需要每次显示就刷新的页面实现 IPageLifecycle；要收扫码的页面实现 IBarcodeHandler。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "原料、配方、称量、历史、设置等页面。";
}
