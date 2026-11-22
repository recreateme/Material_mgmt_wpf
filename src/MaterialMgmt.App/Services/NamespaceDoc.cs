namespace MaterialMgmt.App.Services;

/// <summary>
/// 界面自己的服务，不是写库的那些 Service。
/// DeviceHub 管秤和扫码枪的连接；NavigationService 管当前页；WeighWorkspace 保证只有一个称量窗。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "导航、设备连接、称量窗、提示音。不直接写业务表。";
}
