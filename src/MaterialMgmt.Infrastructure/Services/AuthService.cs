using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private CurrentUser? _session;

    public AuthService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    public CurrentUser? CurrentUser => _session;

    public CurrentUser? Login(string username, string password)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Username == username && u.IsActive);
        if (user is null || !VerifyPassword(password, user.PasswordHash))
            return null;

        _session = new CurrentUser
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
        return _session;
    }

    public void Logout() => _session = null;

    public CurrentUser RequireRole(params string[] roles)
    {
        var u = _session;
        if (u is null || !roles.Contains(u.Role))
            throw new UnauthorizedAccessException(
                $"需要角色：{string.Join(",", roles)}，当前：{(u?.Role ?? "未登录")}");
        return u;
    }

    public string HashPassword(string raw) => BCrypt.Net.BCrypt.HashPassword(raw);

    public bool VerifyPassword(string raw, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(raw, hash); }
        catch { return false; }
    }

    public void ChangePassword(int userId, string oldPassword, string newPassword)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId)
                   ?? throw new InvalidOperationException("用户不存在");
        if (!VerifyPassword(oldPassword, user.PasswordHash))
            throw new InvalidOperationException("原密码不正确");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new InvalidOperationException("新密码至少 6 位");
        user.PasswordHash = HashPassword(newPassword);
        user.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }
}
