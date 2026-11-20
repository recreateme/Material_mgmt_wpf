using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private CurrentUser? _session;

    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);
    private readonly object _lock = new();
    private readonly Dictionary<string, (int Fails, DateTime LockedUntil)> _attempts =
        new(StringComparer.OrdinalIgnoreCase);

    // 出厂种子账号的默认密码（与 DbBootstrap 种子保持一致），登录后强制修改
    private static readonly Dictionary<string, string> SeedDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "88888888",
        ["operator"] = "operator123"
    };

    public TimeSpan? GetLockoutRemaining(string username)
    {
        lock (_lock)
        {
            if (!_attempts.TryGetValue(username, out var a)) return null;
            var left = a.LockedUntil - DateTime.Now;
            return left > TimeSpan.Zero ? left : null;
        }
    }

    public bool IsDefaultPassword(string username, string password)
        => SeedDefaults.TryGetValue(username, out var d) && d == password;

    public AuthService(IDbContextFactory<MaterialMgmtDbContext> factory)
    {
        _factory = factory;
    }

    public CurrentUser? CurrentUser => _session;

    public CurrentUser? Login(string username, string password)
    {
        if (GetLockoutRemaining(username) is not null)
            return null;

        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Username == username && u.IsActive);
        if (user is null || !VerifyPassword(password, user.PasswordHash))
        {
            lock (_lock)
            {
                _attempts.TryGetValue(username, out var a);
                var fails = a.Fails + 1;
                _attempts[username] = fails >= MaxFailures
                    ? (0, DateTime.Now + LockDuration)
                    : (fails, a.LockedUntil);
            }
            return null;
        }

        lock (_lock) _attempts.Remove(username);

        _session = new CurrentUser
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
        return _session;
    }

    public CurrentUser UpdateDisplayName(int userId, string displayName)
    {
        var name = (displayName ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("姓名不能为空");
        if (name.Length > 64) throw new InvalidOperationException("姓名过长（最多 64 字）");

        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId)
                   ?? throw new InvalidOperationException("用户不存在");
        user.DisplayName = name;
        user.UpdatedAt = DateTime.Now;
        db.SaveChanges();

        var refreshed = new CurrentUser
        {
            Id = user.Id, Username = user.Username, DisplayName = user.DisplayName, Role = user.Role
        };
        if (_session?.Id == userId) _session = refreshed;
        return refreshed;
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
