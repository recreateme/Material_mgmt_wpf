using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class UserMgmtPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly DataGrid _grid = new();
    private readonly EmptyState _empty;

    public UserMgmtPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _empty = new EmptyState("还没有业务人员账号", "新增业务人员", () => Edit(null));
        Build();
    }

    public void OnShow() => Refresh();

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new PageHeader("业务人员账号", "业务人员可管理原料、配方并称量；系统管理员只维护账号与设置");
        DockPanel.SetDock(bar, Dock.Top);
        var add = bar.AddAction(UiTheme.PrimaryButton("新增")); add.Click += (_, _) => Edit(null);
        var edit = bar.AddAction(UiTheme.PrimaryButton("编辑")); edit.Click += (_, _) => { if (CurrentRow() is { } r) Edit(r); };
        var reset = bar.AddAction(UiTheme.GhostButton("重置密码")); reset.Click += (_, _) => ResetPw();
        var toggle = bar.AddAction(UiTheme.GhostButton("启用/禁用")); toggle.Click += (_, _) => Toggle();
        var del = bar.AddAction(UiTheme.DangerButton("删除")); del.Click += (_, _) => Delete();

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        GridKit.BindSingleActions(_grid, edit, reset);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 60));
        _grid.Columns.Add(GridKit.Text("用户名", "Item.Username", 140));
        _grid.Columns.Add(GridKit.Text("显示名", "Item.DisplayName", 140));
        _grid.Columns.Add(GridKit.Text("角色", "Item.RoleDisplay", 110));
        _grid.Columns.Add(GridKit.Text("状态", "Item.ActiveDisplay", 90));
        root.Children.Add(bar); root.Children.Add(EmptyState.Host(_grid, _empty)); Content = root;
    }

    private IQueryable<User> Scope(MaterialMgmtDbContext db) =>
        db.Users.Where(u => u.Role == Roles.Business);

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        var rows = GridKit.Wrap(Scope(db).OrderBy(u => u.Id).AsEnumerable().Select(u => new UserRow(u)));
        _grid.ItemsSource = rows;
        _empty.Update(rows.Count, filtered: false);
    }

    private void Edit(UserRow? row)
    {
        var me = _auth.CurrentUser!;
        if (!me.IsSystemAdmin())
        {
            UiFeedback.Warn("仅系统管理员可维护业务人员账号");
            return;
        }

        var dlg = new UserEditDialog(_dbFactory, _auth, _audit, me, row?.Id, row?.Username, row?.DisplayName)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        Refresh();
        UiFeedback.Success(row is null ? "业务人员已创建。" : "已保存。");
    }

    private UserRow? CurrentRow() => GridKit.Current<UserRow>(_grid)?.Item
                                    ?? GridKit.CheckedItems<UserRow>(_grid).FirstOrDefault();

    private void ResetPw()
    {
        if (CurrentRow() is not { } row) return;
        var dlg = new ResetPasswordDialog(_dbFactory, _auth, _audit, row.Id, row.Username)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            UiFeedback.Success($"已重置 {row.Username} 的密码。");
    }

    private void Toggle()
    {
        if (CurrentRow() is not { } row) return;
        var actor = _auth.CurrentUser;
        if (actor is null) { UiFeedback.Hint("当前未登录用户"); return; }
        if (row.Id == actor.Id) { UiFeedback.Hint("不能禁用自己"); return; }
        using var db = _dbFactory.CreateDbContext();
        var u = db.Users.First(x => x.Id == row.Id);
        u.IsActive = !u.IsActive; u.UpdatedAt = DateTime.Now; db.SaveChanges();
        _audit.Log("user", $"{(u.IsActive ? "启用" : "禁用")}用户 {u.Username}", actor.Username);
        Refresh();
        UiFeedback.Success($"用户 {u.Username} 已{(u.IsActive ? "启用" : "禁用")}。");
    }

    private void Delete()
    {
        var actor = _auth.CurrentUser;
        if (actor is null) { UiFeedback.Hint("当前未登录用户"); return; }
        var list = GridKit.CheckedItems<UserRow>(_grid);
        if (list.Count == 0 && CurrentRow() is { } one) list = new List<UserRow> { one };
        if (list.Count == 0) { UiFeedback.Hint("请先勾选要删除的用户"); return; }
        if (list.Any(r => r.Id == actor.Id)) { UiFeedback.Hint("不能删除自己"); return; }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var free = new List<UserRow>();
            var linked = new List<(UserRow Row, int OrderCount)>();
            foreach (var row in list)
            {
                var n = db.WorkOrders.Count(w => w.OperatorId == row.Id);
                if (n > 0) linked.Add((row, n));
                else free.Add(row);
            }

            if (linked.Count == 0)
            {
                if (!ConfirmDialog.Ask($"确认删除 {free.Count} 个用户？此操作不可恢复。", "确认删除", "删除", danger: true))
                    return;
                RemoveUsers(db, free, actor.Username);
                db.SaveChanges();
                Refresh();
                UiFeedback.Success($"已删除 {free.Count} 个用户。");
                return;
            }

            var choice = AskLinkedUserAction(linked, free.Count);
            if (choice == LinkedUserAction.Cancel) return;

            if (choice == LinkedUserAction.DisableOnly)
            {
                foreach (var (row, _) in linked)
                {
                    var u = db.Users.FirstOrDefault(x => x.Id == row.Id);
                    if (u is null || !u.IsActive) continue;
                    u.IsActive = false;
                    u.UpdatedAt = DateTime.Now;
                    _audit.Log("user", $"禁用用户 {u.Username}（删除时因存在工单）", actor.Username);
                }
                if (free.Count > 0 &&
                    ConfirmDialog.Ask($"有工单账号已禁用。是否同时删除其余 {free.Count} 个无工单账号？", "确认", "同时删除", "保留", danger: true))
                {
                    RemoveUsers(db, free, actor.Username);
                }
                db.SaveChanges();
                Refresh();
                UiFeedback.Success(linked.Count > 0 && free.Count == 0
                    ? "已按选择处理关联用户（禁用或删除）。"
                    : "用户状态已更新。");
                return;
            }

            foreach (var (row, _) in linked)
            {
                foreach (var wo in db.WorkOrders.Where(w => w.OperatorId == row.Id))
                    wo.OperatorId = null;
            }
            RemoveUsers(db, linked.Select(x => x.Row).Concat(free).ToList(), actor.Username);
            db.SaveChanges();
            Refresh();
            UiFeedback.Success("已解除工单关联并删除所选用户。");
        }
        catch (DbUpdateException)
        {
            UiFeedback.Warn("删除失败：该用户仍被其它数据引用。请改用「启用/禁用」，或选择解除工单关联后再删。", "无法删除");
        }
        catch (Exception ex)
        {
            UiFeedback.Error($"删除失败：{ex.Message}", "删除失败");
        }
    }

    private void RemoveUsers(MaterialMgmtDbContext db, IEnumerable<UserRow> rows, string actorName)
    {
        foreach (var row in rows)
        {
            var u = db.Users.FirstOrDefault(x => x.Id == row.Id);
            if (u is null) continue;
            db.Users.Remove(u);
            _audit.Log("user", $"删除用户 {row.Username}", actorName);
        }
    }

    private static LinkedUserAction AskLinkedUserAction(List<(UserRow Row, int OrderCount)> linked, int freeCount)
        => LinkedUserActionDialog.Ask(
            linked.Select(x => $"· {x.Row.DisplayName}（{x.Row.Username}）— {x.OrderCount} 条工单"), freeCount);

    private sealed class UserRow
    {
        public UserRow(User u)
        {
            Id = u.Id; Username = u.Username; DisplayName = u.DisplayName; Role = u.Role; IsActive = u.IsActive;
            RoleDisplay = RoleLabels.ToDisplay(u.Role); ActiveDisplay = u.IsActive ? "启用" : "禁用";
        }
        public int Id { get; }
        public string Username { get; }
        public string DisplayName { get; }
        public string Role { get; }
        public bool IsActive { get; }
        public string RoleDisplay { get; }
        public string ActiveDisplay { get; }
    }

}
