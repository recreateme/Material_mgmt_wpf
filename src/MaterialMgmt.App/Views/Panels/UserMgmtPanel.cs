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
        bar.Children.Add(new TextBlock { Text = "用户管理", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        var add = UiTheme.PrimaryButton("新增"); add.Click += (_, _) => Edit(null); bar.Children.Add(add);
        var edit = UiTheme.PrimaryButton("编辑"); edit.Click += (_, _) => { if (CurrentRow() is { } r) Edit(r); }; bar.Children.Add(edit);
        var reset = UiTheme.GhostButton("重置密码"); reset.Click += (_, _) => ResetPw(); bar.Children.Add(reset);
        var toggle = UiTheme.GhostButton("启用/禁用"); toggle.Click += (_, _) => Toggle(); bar.Children.Add(toggle);
        var del = UiTheme.DangerButton("删除"); del.Click += (_, _) => Delete(); bar.Children.Add(del);
        GridKit.AddSelectButtons(bar, _grid);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 60));
        _grid.Columns.Add(GridKit.Text("用户名", "Item.Username", 140));
        _grid.Columns.Add(GridKit.Text("显示名", "Item.DisplayName", 140));
        _grid.Columns.Add(GridKit.Text("角色", "Item.RoleDisplay", 110));
        _grid.Columns.Add(GridKit.Text("状态", "Item.ActiveDisplay", 90));
        root.Children.Add(bar); root.Children.Add(_grid); Content = root;
    }

    private IQueryable<User> Scope(MaterialMgmtDbContext db)
    {
        var me = _auth.CurrentUser!;
        if (me.IsRoot()) return db.Users.Where(u => u.Role != Roles.Root || u.Id == me.Id);
        return db.Users.Where(u => u.Role == Roles.Operator);
    }

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        _grid.ItemsSource = GridKit.Wrap(Scope(db).OrderBy(u => u.Id).AsEnumerable().Select(u => new UserRow(u)));
    }

    private void Edit(UserRow? row)
    {
        var me = _auth.CurrentUser!;
        var userBox = new TextBox { Height = 30, Text = row?.Username ?? "", IsEnabled = row is null, Margin = new Thickness(0, 4, 0, 8) };
        var display = new TextBox { Height = 30, Text = row?.DisplayName ?? "", Margin = new Thickness(0, 4, 0, 8) };
        var role = new ComboBox { Height = 30, Margin = new Thickness(0, 4, 0, 8) };
        if (me.IsRoot()) { role.Items.Add(Roles.Admin); role.Items.Add(Roles.Operator); }
        else role.Items.Add(Roles.Operator);
        role.SelectedItem = row?.Role ?? Roles.Operator;
        var pw = new PasswordBox { Height = 30, Margin = new Thickness(0, 4, 0, 8), Visibility = row is null ? Visibility.Visible : Visibility.Collapsed };

        var dlg = new Window { Title = row is null ? "新增用户" : "编辑用户", Width = 380, Height = 340, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "用户名" }); stack.Children.Add(userBox);
        stack.Children.Add(new TextBlock { Text = "显示名" }); stack.Children.Add(display);
        stack.Children.Add(new TextBlock { Text = "角色" }); stack.Children.Add(role);
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
                    db.Users.Add(new User
                    {
                        Username = userBox.Text.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(display.Text) ? userBox.Text.Trim() : display.Text.Trim(),
                        Role = (string)role.SelectedItem!,
                        PasswordHash = _auth.HashPassword(pw.Password),
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                    _audit.Log("user", $"创建用户 {userBox.Text.Trim()}", me.Username);
                }
                else
                {
                    var u = db.Users.First(x => x.Id == row.Id);
                    if (u.Id == me.Id && (string)role.SelectedItem! != u.Role)
                    { MessageBox.Show(dlg, "不能修改自己的角色"); return; }
                    u.DisplayName = display.Text.Trim();
                    u.Role = (string)role.SelectedItem!;
                    u.UpdatedAt = DateTime.Now;
                    _audit.Log("user", $"编辑用户 {u.Username}", me.Username);
                }
                db.SaveChanges();
                dlg.DialogResult = true; dlg.Close(); Refresh();
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
        var dlg = new Window { Title = "重置密码", Width = 340, Height = 180, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
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
    }

    private void Delete()
    {
        var actor = _auth.CurrentUser;
        if (actor is null) { MessageBox.Show("当前未登录用户"); return; }
        var list = GridKit.CheckedItems<UserRow>(_grid);
        if (list.Count == 0 && CurrentRow() is { } one) list = new List<UserRow> { one };
        if (list.Count == 0) { MessageBox.Show("请先勾选要删除的用户"); return; }
        if (list.Any(r => r.Id == actor.Id)) { MessageBox.Show("不能删除自己"); return; }
        if (MessageBox.Show($"确认删除 {list.Count} 个用户？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using var db = _dbFactory.CreateDbContext();
        foreach (var row in list)
        {
            var u = db.Users.FirstOrDefault(x => x.Id == row.Id);
            if (u is null) continue;
            db.Users.Remove(u);
            _audit.Log("user", $"删除用户 {row.Username}", actor.Username);
        }
        db.SaveChanges();
        Refresh();
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
