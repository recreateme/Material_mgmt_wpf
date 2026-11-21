using Microsoft.EntityFrameworkCore;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.Infrastructure.Services;

/// <summary>
/// 业务人员账号维护（系统管理员使用）。只操作 <see cref="Roles.Business"/> 账号；
/// 校验失败抛 <see cref="InvalidOperationException"/>，消息可直接给用户看。
/// </summary>
public sealed class UserService
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _factory;
    private readonly IAuthService _auth;

    public UserService(IDbContextFactory<MaterialMgmtDbContext> factory, IAuthService auth)
    {
        _factory = factory;
        _auth = auth;
    }

    public List<User> BusinessUsers()
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().Where(u => u.Role == Roles.Business).OrderBy(u => u.Id).ToList();
    }

    public bool UsernameExists(string username)
    {
        var name = username.Trim();
        using var db = _factory.CreateDbContext();
        return db.Users.Any(u => u.Username == name);
    }

    public User CreateBusiness(string username, string? displayName, string password)
    {
        var name = username.Trim();
        if (name.Length == 0) throw new InvalidOperationException("请填写用户名");
        if (PasswordPolicy.Validate(name, password) is { } problem) throw new InvalidOperationException(problem);

        using var db = _factory.CreateDbContext();
        if (db.Users.Any(u => u.Username == name)) throw new InvalidOperationException("该用户名已存在");
        var user = new User
        {
            Username = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
            Role = Roles.Business,
            PasswordHash = _auth.HashPassword(password),
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public User UpdateDisplayName(int userId, string displayName)
    {
        using var db = _factory.CreateDbContext();
        var u = Business(db, userId);
        u.DisplayName = displayName.Trim();
        u.UpdatedAt = DateTime.Now;
        db.SaveChanges();
        return u;
    }

    public void ResetPassword(int userId, string password)
    {
        using var db = _factory.CreateDbContext();
        var u = Business(db, userId);
        if (PasswordPolicy.Validate(u.Username, password) is { } problem) throw new InvalidOperationException(problem);
        u.PasswordHash = _auth.HashPassword(password);
        u.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    /// <summary>切换启用状态，返回切换后的账号。</summary>
    public User ToggleActive(int userId)
    {
        using var db = _factory.CreateDbContext();
        var u = Business(db, userId);
        u.IsActive = !u.IsActive;
        u.UpdatedAt = DateTime.Now;
        db.SaveChanges();
        return u;
    }

    /// <summary>各账号作为操作员关联的工单数（含已删除工单）；无工单的账号不出现在结果中。</summary>
    public Dictionary<int, int> OrderCounts(IEnumerable<int> userIds)
    {
        var ids = userIds.ToList();
        using var db = _factory.CreateDbContext();
        return db.WorkOrders
            .Where(w => w.OperatorId != null && ids.Contains(w.OperatorId.Value))
            .GroupBy(w => w.OperatorId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
    }

    /// <summary>禁用账号（保留账号与工单），返回本次由启用变为禁用的账号。</summary>
    public List<User> Disable(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        var users = db.Users.Where(u => ids.Contains(u.Id) && u.Role == Roles.Business && u.IsActive).ToList();
        foreach (var u in users)
        {
            u.IsActive = false;
            u.UpdatedAt = DateTime.Now;
        }
        db.SaveChanges();
        return users;
    }

    /// <summary>
    /// 删除账号，单事务。<paramref name="unlinkOrders"/> 为 true 时先把这些账号的工单操作员 Id 置空
    /// （工单保留操作员姓名）；为 false 时若有关联工单则抛异常、不删除任何账号。返回删除的账号。
    /// </summary>
    public List<User> Delete(IEnumerable<int> userIds, bool unlinkOrders)
    {
        var ids = userIds.Distinct().ToList();
        using var db = _factory.CreateDbContext();
        var orders = db.WorkOrders.Where(w => w.OperatorId != null && ids.Contains(w.OperatorId.Value)).ToList();
        if (orders.Count > 0 && !unlinkOrders)
            throw new InvalidOperationException("所选账号仍关联工单，未删除任何账号");
        foreach (var wo in orders) wo.OperatorId = null;

        var users = db.Users.Where(u => ids.Contains(u.Id) && u.Role == Roles.Business).ToList();
        db.Users.RemoveRange(users);
        db.SaveChanges();
        return users;
    }

    private static User Business(MaterialMgmtDbContext db, int userId)
    {
        var u = db.Users.FirstOrDefault(x => x.Id == userId)
                ?? throw new InvalidOperationException("该账号已不存在，请刷新列表");
        if (u.Role != Roles.Business) throw new InvalidOperationException("只能维护业务人员账号");
        return u;
    }
}
