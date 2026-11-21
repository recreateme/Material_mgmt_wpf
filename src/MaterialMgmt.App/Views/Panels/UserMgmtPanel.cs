using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Services;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class UserMgmtPanel : UserControl, IPageLifecycle
{
    private readonly UserService _users;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly DataGrid _grid = new();
    private readonly EmptyState _empty;

    public UserMgmtPanel(UserService users, IAuthService auth, IAuditLog audit)
    {
        _users = users;
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

    private void Refresh()
    {
        var rows = GridKit.Wrap(_users.BusinessUsers().Select(u => new UserRow(u)));
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

        var dlg = new UserEditDialog(_users, _audit, me, row?.Id, row?.Username, row?.DisplayName)
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
        var dlg = new ResetPasswordDialog(_users, _audit, _auth.CurrentUser?.Username ?? "", row.Id, row.Username)
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
        User u;
        try { u = _users.ToggleActive(row.Id); }
        catch (Exception ex) { UiFeedback.Warn(ex.Message); Refresh(); return; }
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
            var counts = _users.OrderCounts(list.Select(r => r.Id));
            var linked = list.Where(r => counts.ContainsKey(r.Id)).Select(r => (Row: r, OrderCount: counts[r.Id])).ToList();
            var free = list.Where(r => !counts.ContainsKey(r.Id)).ToList();

            if (linked.Count == 0)
            {
                if (!ConfirmDialog.Ask($"确认删除 {free.Count} 个用户？此操作不可恢复。", "确认删除", "删除", danger: true))
                    return;
                LogDeleted(_users.Delete(free.Select(r => r.Id), unlinkOrders: false), actor.Username);
                Refresh();
                UiFeedback.Success($"已删除 {free.Count} 个用户。");
                return;
            }

            var choice = AskLinkedUserAction(linked, free.Count);
            if (choice == LinkedUserAction.Cancel) return;

            if (choice == LinkedUserAction.DisableOnly)
            {
                foreach (var u in _users.Disable(linked.Select(x => x.Row.Id)))
                    _audit.Log("user", $"禁用用户 {u.Username}（删除时因存在工单）", actor.Username);
                if (free.Count > 0 &&
                    ConfirmDialog.Ask($"有工单账号已禁用。是否同时删除其余 {free.Count} 个无工单账号？", "确认", "同时删除", "保留", danger: true))
                {
                    LogDeleted(_users.Delete(free.Select(r => r.Id), unlinkOrders: false), actor.Username);
                }
                Refresh();
                UiFeedback.Success(linked.Count > 0 && free.Count == 0
                    ? "已按选择处理关联用户（禁用或删除）。"
                    : "用户状态已更新。");
                return;
            }

            LogDeleted(_users.Delete(list.Select(r => r.Id), unlinkOrders: true), actor.Username);
            Refresh();
            UiFeedback.Success("已解除工单关联并删除所选用户。");
        }
        catch (Exception ex)
        {
            UiFeedback.Warn($"删除失败：{ex.GetBaseException().Message}", "无法删除");
            Refresh();
        }
    }

    private void LogDeleted(IEnumerable<User> users, string actorName)
    {
        foreach (var u in users)
            _audit.Log("user", $"删除用户 {u.Username}", actorName);
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
