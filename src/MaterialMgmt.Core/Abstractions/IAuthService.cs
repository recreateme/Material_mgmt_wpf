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
}
