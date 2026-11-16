namespace MaterialMgmt.Core.Auth;

/// <summary>
/// v4.2 角色迁移：admin→system_admin，operator→business，删除 root。
/// 业务表（原料/配方/工单等）不在此处理。
/// </summary>
public static class RoleMigration
{
    public sealed record UserSnap(int Id, string Username, string Role);

    public sealed record PlanResult(
        IReadOnlyList<(int Id, string NewRole)> Updates,
        IReadOnlyList<int> DeleteIds,
        string? Error);

    /// <summary>将历史/当前角色规范为新角色；root 返回 null 表示应删除。</summary>
    public static string? NormalizeRole(string role) => role switch
    {
        Roles.SystemAdmin or Roles.LegacyAdmin => Roles.SystemAdmin,
        Roles.Business or Roles.LegacyOperator => Roles.Business,
        Roles.LegacyRoot => null,
        _ => null
    };

    public static PlanResult Plan(IReadOnlyList<UserSnap> users)
    {
        var updates = new List<(int, string)>();
        var deletes = new List<int>();

        foreach (var u in users)
        {
            var mapped = NormalizeRole(u.Role);
            if (mapped is null)
            {
                if (u.Role == Roles.LegacyRoot || string.Equals(u.Username, "root", StringComparison.OrdinalIgnoreCase))
                    deletes.Add(u.Id);
                else
                    return new PlanResult(updates, deletes, $"无法识别的角色「{u.Role}」（用户 {u.Username}）");
                continue;
            }

            if (u.Role != mapped)
                updates.Add((u.Id, mapped));
        }

        // 预演迁移后的角色集合
        var after = new Dictionary<int, string>();
        foreach (var u in users)
            after[u.Id] = u.Role;
        foreach (var (id, role) in updates)
            after[id] = role;
        foreach (var id in deletes)
            after.Remove(id);

        var hasAdmin = after.Values.Any(r => NormalizeRole(r) == Roles.SystemAdmin);
        if (!hasAdmin)
            return new PlanResult(updates, deletes,
                "迁移后将没有系统管理员，已中止删除 root。请先保留或创建 admin。");

        return new PlanResult(updates, deletes, null);
    }
}
