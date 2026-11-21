using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

public sealed class ChangePasswordDialog : DialogWindow
{
    private readonly IAuthService _auth;
    private readonly int _userId;
    private readonly PasswordBox _old;
    private readonly PasswordBox _nw;
    private readonly PasswordBox _confirm;

    public ChangePasswordDialog(IAuthService auth, int userId) : base("修改密码", 380)
    {
        _auth = auth;
        _userId = userId;
        _old = AddField("原密码", new PasswordBox());
        _nw = AddField("新密码", new PasswordBox(), PasswordPolicy.Hint);
        _confirm = AddField("确认新密码", new PasswordBox());
    }

    protected override void OnConfirm()
    {
        if (string.IsNullOrEmpty(_old.Password)) { ShowError(_old, "请输入原密码"); return; }
        if (string.IsNullOrEmpty(_nw.Password)) { ShowError(_nw, "请输入新密码"); return; }
        if (_nw.Password != _confirm.Password) { ShowError(_confirm, "两次输入的新密码不一致"); return; }
        try
        {
            _auth.ChangePassword(_userId, _old.Password, _nw.Password);
        }
        catch (Exception ex)
        {
            // 服务层的失败原因（原密码错误、新密码不合规等）直接显示在对应字段旁
            ShowError(ex.Message.Contains("原密码不正确", StringComparison.Ordinal) ? _old : _nw, ex.Message);
            return;
        }
        Accept();
        Toast.Success("密码已修改");
    }
}
