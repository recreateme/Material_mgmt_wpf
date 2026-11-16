namespace MaterialMgmt.Core.Auth;

public sealed class CurrentUser
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";

    public bool IsSystemAdmin() => Role == Roles.SystemAdmin;
    public bool IsBusiness() => Role == Roles.Business;
}

/// <summary>系统角色。库内存储值：system_admin / business。</summary>
public static class Roles
{
    public const string SystemAdmin = "system_admin";
    public const string Business = "business";

    /// <summary>历史角色（仅迁移识别）。</summary>
    public const string LegacyRoot = "root";
    public const string LegacyAdmin = "admin";
    public const string LegacyOperator = "operator";
}
