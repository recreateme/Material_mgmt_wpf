namespace MaterialMgmt.Core.Auth;

/// <summary>
/// 登录之后的身份：两种角色各能进哪些页、密码规则、旧角色名如何迁到现在的名字。
/// 密码哈希和失败锁定次数在 Infrastructure 的 AuthService。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "角色、密码规则、当前登录用户。";
}
