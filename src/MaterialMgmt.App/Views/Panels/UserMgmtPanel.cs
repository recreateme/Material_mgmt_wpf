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

namespace MaterialMgmt.App.Views.Panels;

public sealed class UserMgmtPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly DataGrid _grid = new();

    public UserMgmtPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        Build();
    }

    public void OnShow() => Refresh();

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        bar.Children.Add(new TextBlock { Text = "业务人员账号", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        var add = UiTheme.PrimaryButton("新增"); add.Click += (_, _) => Edit(null); bar.Children.Add(add);
        var edit = UiTheme.PrimaryButton("编辑"); edit.Click += (_, _) => { if (CurrentRow() is { } r) Edit(r); }; bar.Children.Add(edit);
        var reset = UiTheme.GhostButton("重置密码"); reset.Click += (_, _) => ResetPw(); bar.Children.Add(reset);
        var toggle = UiTheme.GhostButton("启用/禁用"); toggle.Click += (_, _) => Toggle(); bar.Children.Add(toggle);
        var del = UiTheme.DangerButton("删除"); del.Click += (_, _) => Delete(); bar.Children.Add(del);
        GridKit.AddSelectButtons(bar, _grid);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        GridKit.BindSingleActions(_grid, edit, reset);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 60));
        _grid.Columns.Add(GridKit.Text("用户名", "Item.Username", 140));
        _grid.Columns.Add(GridKit.Text("显示名", "Item.DisplayName", 140));
        _grid.Columns.Add(GridKit.Text("角色", "Item.RoleDisplay", 110));
        _grid.Columns.Add(GridKit.Text("状态", "Item.ActiveDisplay", 90));
        root.Children.Add(bar); root.Children.Add(_grid); Content = root;
    }

    private IQueryable<User> Scope(MaterialMgmtDbContext db) =>
        db.Users.Where(u => u.Role == Roles.Business);

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        _grid.ItemsSource = GridKit.Wrap(Scope(db).OrderBy(u => u.Id).AsEnumerable().Select(u => new UserRow(u)));
    }

    private void Edit(UserRow? row)
    {
        var me = _auth.CurrentUser!;
        if (!me.IsSystemAdmin())
        {
            MessageBox.Show("仅系统管理员可维护业务人员账号");
            return;
        }

        var userBox = new TextBox { Height = 30, Text = row?.Username ?? "", IsEnabled = row is null, Margin = new Thickness(0, 4, 0, 8) };
        var display = new TextBox { Height = 30, Text = row?.DisplayName ?? "", Margin = new Thickness(0, 4, 0, 8) };
        var pw = new PasswordBox { Height = 30, Margin = new Thickness(0, 4, 0, 8), Visibility = row is null ? Visibility.Visible : Visibility.Collapsed };

        var dlg = new Window { Title = row is null ? "新增业务人员" : "编辑业务人员", Width = 400, SizeToContent = SizeToContent.Height, MinHeight = 260, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "用户名" }); stack.Children.Add(userBox);
        stack.Children.Add(new TextBlock { Text = "显示名" }); stack.Children.Add(display);
        stack.Children.Add(UiTheme.Hint("角色固定为业务人员（可管理原料/配方/称量）"));
        if (row is null) { stack.Children.Add(new TextBlock { Text = "初始密码" }); stack.Children.Add(pw); }
        var ok = UiTheme.PrimaryButton("保存");
        ok.Click += (_, _) =>
        {
            try
            {
                using var db = _dbFactory.CreateDbContext();
                if (row is null)
                {
                    if (string.IsNullOrWhiteSpace(userBox.Text) || string.IsNullOrWhiteSpace(pw.Password))
                    { MessageBox.Show(dlg, "请填写用户名和密码"); return; }
                    if (pw.Password.Length < 6)
                    { MessageBox.Show(dlg, "密码至少 6 位"); return; }
                    var name = userBox.Text.Trim();
                    db.Users.Add(new User
                    {
                        Username = name,
                        DisplayName = string.IsNullOrWhiteSpace(display.Text) ? name : display.Text.Trim(),
                        Role = Roles.Business,
                        PasswordHash = _auth.HashPassword(pw.Password),
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                    _audit.Log("user", $"创建业务人员 {name}", me.Username);
                }
                else
                {
                    var u = db.Users.First(x => x.Id == row.Id);
                    if (u.Role != Roles.Business)
                    { MessageBox.Show(dlg, "只能编辑业务人员账号"); return; }
                    u.DisplayName = display.Text.Trim();
                    u.UpdatedAt = DateTime.Now;
                    _audit.Log("user", $"编辑业务人员 {u.Username}", me.Username);
                }
                db.SaveChanges();
                dlg.DialogResult = true; dlg.Close(); Refresh();
                UiFeedback.Success(row is null ? "业务人员已创建。" : "已保存。");
            }
            catch (Exception ex) { MessageBox.Show(dlg, ex.Message); }
        };
        stack.Children.Add(ok); dlg.Content = stack; dlg.ShowDialog();
    }

    private UserRow? CurrentRow() => GridKit.Current<UserRow>(_grid)?.Item
                                    ?? GridKit.CheckedItems<UserRow>(_grid).FirstOrDefault();

    private void ResetPw()
    {
        if (CurrentRow() is not { } row) return;
        var pw = new PasswordBox { Height = 30, Margin = new Thickness(0, 4, 0, 8) };
        var dlg = new Window { Title = "重置密码", Width = 340, SizeToContent = SizeToContent.Height, MinHeight = 160, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "新密码" }); stack.Children.Add(pw);
        var ok = UiTheme.PrimaryButton("确定");
        ok.Click += (_, _) =>
        {
            if (pw.Password.Length < 6) { MessageBox.Show(dlg, "密码至少 6 位"); return; }
            using var db = _dbFactory.CreateDbContext();
            var u = db.Users.First(x => x.Id == row.Id);
            u.PasswordHash = _auth.HashPassword(pw.Password);
            u.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            _audit.Log("user", $"重置密码 {u.Username}", _auth.CurrentUser?.Username ?? "");
            dlg.DialogResult = true; dlg.Close();
            UiFeedback.Success($"已重置 {row.Username} 的密码。");
        };
        stack.Children.Add(ok); dlg.Content = stack; dlg.ShowDialog();
    }

    private void Toggle()
    {
        if (CurrentRow() is not { } row) return;
        var actor = _auth.CurrentUser;
        if (actor is null) { MessageBox.Show("当前未登录用户"); return; }
        if (row.Id == actor.Id) { MessageBox.Show("不能禁用自己"); return; }
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
        if (actor is null) { MessageBox.Show("当前未登录用户"); return; }
        var list = GridKit.CheckedItems<UserRow>(_grid);
        if (list.Count == 0 && CurrentRow() is { } one) list = new List<UserRow> { one };
        if (list.Count == 0) { MessageBox.Show("请先勾选要删除的用户"); return; }
        if (list.Any(r => r.Id == actor.Id)) { MessageBox.Show("不能删除自己"); return; }

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
                if (MessageBox.Show($"确认删除 {free.Count} 个用户？此操作不可恢复。", "确认",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
                    MessageBox.Show($"有工单账号已禁用。是否同时删除其余 {free.Count} 个无工单账号？", "确认",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
            MessageBox.Show("删除失败：该用户仍被其它数据引用。请改用「启用/禁用」，或选择解除工单关联后再删。",
                "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private enum LinkedUserAction { Cancel, DisableOnly, UnlinkAndDelete }

    private LinkedUserAction AskLinkedUserAction(List<(UserRow Row, int OrderCount)> linked, int freeCount)
    {
        var lines = string.Join("\n", linked.Select(x => $"· {x.Row.DisplayName}（{x.Row.Username}）— {x.OrderCount} 条工单"));
        var extra = freeCount > 0 ? $"\n另有 {freeCount} 个账号无工单。" : "";

        var dlg = new Window
        {
            Title = "无法直接删除",
            Width = 480,
            Height = 320,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var result = LinkedUserAction.Cancel;
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = "以下账号仍关联工单，直接删除会失败：",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = lines + extra,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "请选择处理方式：\n• 仅禁用：保留账号与工单，禁止登录\n• 解除关联并删除：工单保留（操作员姓名保留），账号删除",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = UiTheme.GhostButton("取消");
        cancel.Click += (_, _) => { result = LinkedUserAction.Cancel; dlg.Close(); };
        var disable = UiTheme.GhostButton("仅禁用");
        disable.Click += (_, _) => { result = LinkedUserAction.DisableOnly; dlg.Close(); };
        var unlink = UiTheme.DangerButton("解除关联并删除");
        unlink.Click += (_, _) => { result = LinkedUserAction.UnlinkAndDelete; dlg.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(disable);
        buttons.Children.Add(unlink);
        stack.Children.Add(buttons);

        dlg.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dlg.ShowDialog();
        return result;
    }

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
