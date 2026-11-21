using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>新增 / 编辑业务人员。userId 为 null 表示新增。</summary>
public sealed class UserEditDialog : DialogWindow
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly CurrentUser _actor;
    private readonly int? _userId;
    private readonly TextBox _username;
    private readonly TextBox _display;
    private readonly PasswordBox? _password;

    public UserEditDialog(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit,
        CurrentUser actor, int? userId, string? username, string? displayName)
        : base(userId is null ? "新增业务人员" : "编辑业务人员", 400)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _actor = actor;
        _userId = userId;

        _username = AddField("用户名", new TextBox { Text = username ?? "", IsEnabled = userId is null });
        _display = AddField("显示名", new TextBox { Text = displayName ?? "" },
            "角色固定为业务人员（可管理原料/配方/称量）");
        if (userId is null)
            _password = AddField("初始密码", new PasswordBox(), PasswordPolicy.Hint);
    }

    protected override void OnConfirm()
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            if (_userId is null)
            {
                var name = _username.Text.Trim();
                if (name.Length == 0) { ShowError(_username, "请填写用户名"); return; }
                if (_password is null) return;
                if (PasswordPolicy.Validate(name, _password.Password) is { } problem)
                { ShowError(_password, problem); return; }
                if (db.Users.Any(u => u.Username == name)) { ShowError(_username, "该用户名已存在"); return; }

                db.Users.Add(new User
                {
                    Username = name,
                    DisplayName = string.IsNullOrWhiteSpace(_display.Text) ? name : _display.Text.Trim(),
                    Role = Roles.Business,
                    PasswordHash = _auth.HashPassword(_password.Password),
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
                _audit.Log("user", $"创建业务人员 {name}", _actor.Username);
            }
            else
            {
                var u = db.Users.First(x => x.Id == _userId);
                if (u.Role != Roles.Business) { ShowError(_display, "只能编辑业务人员账号"); return; }
                u.DisplayName = _display.Text.Trim();
                u.UpdatedAt = DateTime.Now;
                _audit.Log("user", $"编辑业务人员 {u.Username}", _actor.Username);
            }
            db.SaveChanges();
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_display, ex.Message);
        }
    }
}

public enum LinkedUserAction { Cancel, DisableOnly, UnlinkAndDelete }

/// <summary>要删除的账号仍关联工单时，选择“仅禁用”或“解除关联并删除”。</summary>
public sealed class LinkedUserActionDialog : DialogWindow
{
    public LinkedUserAction Result { get; private set; } = LinkedUserAction.Cancel;

    private LinkedUserActionDialog(IEnumerable<string> linkedLines, int freeCount)
        : base("无法直接删除", 500, "解除关联并删除")
    {
        Body.Children.Add(new TextBlock
        {
            Text = "以下账号仍关联工单，直接删除会失败：",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var extra = freeCount > 0 ? $"\n另有 {freeCount} 个账号无工单。" : "";
        Body.Children.Add(new TextBlock
        {
            Text = string.Join("\n", linkedLines) + extra,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        Body.Children.Add(UiTheme.Hint(
            "请选择处理方式：\n• 仅禁用：保留账号与工单，禁止登录\n• 解除关联并删除：工单保留（操作员姓名保留），账号删除"));

        if (Application.Current?.TryFindResource("DangerButtonStyle") is Style danger)
            ConfirmButton.Style = danger;
        ConfirmButton.IsDefault = false;
        var disable = AddFooterButton(UiTheme.GhostButton("仅禁用"));
        disable.Click += (_, _) =>
        {
            Result = LinkedUserAction.DisableOnly;
            CloseWith(true);
        };
        Loaded += (_, _) => (CancelButton ?? disable).Focus();
    }

    protected override void OnConfirm()
    {
        Result = LinkedUserAction.UnlinkAndDelete;
        Accept();
    }

    public static LinkedUserAction Ask(IEnumerable<string> linkedLines, int freeCount)
    {
        var dlg = new LinkedUserActionDialog(linkedLines, freeCount);
        dlg.ShowDialog();
        return dlg.Result;
    }
}

/// <summary>系统管理员重置业务人员密码。</summary>
public sealed class ResetPasswordDialog : DialogWindow
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly int _userId;
    private readonly string _username;
    private readonly PasswordBox _password;

    public ResetPasswordDialog(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit,
        int userId, string username)
        : base("重置密码", 360, "确定")
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _userId = userId;
        _username = username;
        Body.Children.Add(new TextBlock
        {
            Text = $"为账号「{username}」设置新密码。",
            Foreground = UiTheme.Brush(UiTheme.Muted),
            Margin = new Thickness(0, 0, 0, 12)
        });
        _password = AddField("新密码", new PasswordBox(), PasswordPolicy.Hint);
    }

    protected override void OnConfirm()
    {
        if (PasswordPolicy.Validate(_username, _password.Password) is { } problem)
        { ShowError(_password, problem); return; }
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var u = db.Users.First(x => x.Id == _userId);
            u.PasswordHash = _auth.HashPassword(_password.Password);
            u.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            _audit.Log("user", $"重置密码 {u.Username}", _auth.CurrentUser?.Username ?? "");
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_password, ex.Message);
        }
    }
}
