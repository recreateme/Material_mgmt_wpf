namespace MaterialMgmt.Core.Auth;

/// <summary>
/// 密码规则（改密、新增账号、重置密码共用）：至少 6 位、不等于用户名、不能用出厂默认密码。
/// 只约束之后设置的新密码，已有账号的密码不受影响。
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 6;

    public const string Hint = "至少 6 位，不能与用户名相同";

    /// <summary>出厂种子账号及其默认密码（仅空库时创建，登录后强制修改）。</summary>
    public static readonly IReadOnlyDictionary<string, string> SeedPasswords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = "88888888",
            ["operator"] = "operator123"
        };

    /// <summary>合规返回 null，否则返回给用户看的原因。</summary>
    public static string? Validate(string username, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return "请输入密码";
        if (password.Length < MinLength) return $"密码至少 {MinLength} 位";
        if (string.Equals(password, username?.Trim(), StringComparison.OrdinalIgnoreCase))
            return "密码不能与用户名相同";
        if (SeedPasswords.Values.Contains(password)) return "不能使用出厂默认密码";
        return null;
    }

    public static bool IsSeedDefault(string username, string password)
        => SeedPasswords.TryGetValue(username, out var d) && d == password;
}
