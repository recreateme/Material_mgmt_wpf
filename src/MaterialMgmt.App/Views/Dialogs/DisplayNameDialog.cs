using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>首次登录时填写姓名（显示名），用于欢迎语、工单操作员与配方创建人。可跳过。</summary>
public sealed class DisplayNameDialog : DialogWindow
{
    private readonly IAuthService _auth;
    private readonly CurrentUser _user;
    private readonly TextBox _name;

    public CurrentUser? UpdatedUser { get; private set; }

    public DisplayNameDialog(IAuthService auth, CurrentUser user) : base("请填写您的姓名", 400, "保存", "以后再说")
    {
        _auth = auth;
        _user = user;

        var current = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        Body.Children.Add(new TextBlock
        {
            Text = $"当前显示为「{current}」。\n请填写真实姓名，登录欢迎语与工单操作员将显示您的名字。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            Margin = new Thickness(0, 0, 0, 14)
        });
        _name = AddField("姓名", new TextBox { MaxLength = 64 });
    }

    protected override void OnConfirm()
    {
        try
        {
            UpdatedUser = _auth.UpdateDisplayName(_user.Id, _name.Text);
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_name, ex.Message);
        }
    }
}
