using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.Core.Abstractions;

public interface IAuthService
{
    CurrentUser? CurrentUser { get; }
    CurrentUser? Login(string username, string password);
    void Logout();
    CurrentUser RequireRole(params string[] roles);
    string HashPassword(string raw);
    bool VerifyPassword(string raw, string hash);
    void ChangePassword(int userId, string oldPassword, string newPassword);

    /// <summary>更新当前用户的显示名（姓名），并返回刷新后的会话用户。</summary>
    CurrentUser UpdateDisplayName(int userId, string displayName);

    /// <summary>该账号当前是否处于登录锁定（连续失败过多）；未锁定返回 null。</summary>
    TimeSpan? GetLockoutRemaining(string username);

    /// <summary>输入的凭据是否仍是出厂种子账号的默认密码（需强制修改）。</summary>
    bool IsDefaultPassword(string username, string password);
}
