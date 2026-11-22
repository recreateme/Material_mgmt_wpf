namespace MaterialMgmt.Core;

/// <summary>
/// 业务规则层。不引用 WPF，不打开串口，不访问数据库。
/// 界面和 Infrastructure 可以调用这里；这里不能反过来调用它们。
/// 子空间：Entities 数据形状，Workflow 称量状态机，Orders / Formulas / Auth / Codes 规则，Hardware 设备侧的常量，Abstractions 给 Infrastructure 实现的接口。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "与界面、串口、数据库无关的规则和数据形状。";
}
