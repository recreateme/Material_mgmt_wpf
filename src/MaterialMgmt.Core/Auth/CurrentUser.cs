namespace MaterialMgmt.Core.Auth;

public sealed class CurrentUser
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";

    private static readonly HashSet<string> PlaceholderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "系统管理员", "业务人员", "管理人员", "开发人员", "操作员", "操作人员",
        "admin", "operator", "root"
    };

    /// <summary>显示名为空、等于账号名或只是角色称呼（如“业务人员”）时，欢迎语无法称呼到具体的人。</summary>
    public bool HasPlaceholderName
        => string.IsNullOrWhiteSpace(DisplayName)
           || string.Equals(DisplayName.Trim(), Username, StringComparison.OrdinalIgnoreCase)
           || PlaceholderNames.Contains(DisplayName.Trim());

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
