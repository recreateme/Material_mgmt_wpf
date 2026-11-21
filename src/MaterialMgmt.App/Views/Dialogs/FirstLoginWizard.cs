using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>
/// 首次登录向导：仍是出厂默认密码时先改密（必须），显示名仍是角色称呼时再填姓名（可跳过）。
/// 两步在同一个窗口里完成；只需要其中一步时只显示那一步。
/// </summary>
public sealed class FirstLoginWizard : DialogWindow
{
    private readonly IAuthService _auth;
    private readonly CurrentUser _user;
    private readonly string? _currentPassword;
    private readonly bool _askName;
    private readonly int _totalSteps;
    private readonly TextBlock _stepTitle;
    private readonly TextBlock _stepNote;
    private readonly PasswordBox? _newPassword;
    private readonly PasswordBox? _confirm;
    private readonly TextBox? _name;
    private bool _onNameStep;

    /// <summary>默认密码已改掉（需要改密时，未改则不得进入系统）。</summary>
    public bool PasswordChanged { get; private set; }

    /// <summary>填写姓名后的会话用户；跳过时为 null。</summary>
    public CurrentUser? NamedUser { get; private set; }

    /// <param name="currentPassword">仍是默认密码时传入刚登录用的密码，向导据此改密，不再让用户重输；不需改密传 null。</param>
    public FirstLoginWizard(IAuthService auth, CurrentUser user, string? currentPassword, bool askName)
        : base("首次登录设置", 420,
            currentPassword is null ? "保存" : askName ? "下一步" : "完成",
            currentPassword is null ? "以后再说" : "退出")
    {
        _auth = auth;
        _user = user;
        _currentPassword = currentPassword;
        _askName = askName;
        _totalSteps = (currentPassword is null ? 0 : 1) + (askName ? 1 : 0);

        _stepTitle = new TextBlock
        {
            FontSize = UiTheme.FontSubtitle,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _stepNote = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            Margin = new Thickness(0, 0, 0, 14)
        };
        Body.Children.Add(_stepTitle);
        Body.Children.Add(_stepNote);

        if (currentPassword is not null)
        {
            _newPassword = AddField("新密码", new PasswordBox(), PasswordPolicy.Hint);
            _confirm = AddField("确认新密码", new PasswordBox());
        }
        if (askName)
            _name = AddField("姓名", new TextBox { MaxLength = 64 });

        if (currentPassword is not null) ShowPasswordStep();
        else ShowNameStep();
    }

    protected override void OnConfirm()
    {
        if (_onNameStep) SaveName();
        else SavePassword();
    }

    private void ShowPasswordStep()
    {
        _stepTitle.Text = StepPrefix(1) + "修改默认密码";
        _stepNote.Text = "当前仍在使用出厂默认密码，修改后才能进入系统。";
        if (_name is not null) Group(_name).Visibility = Visibility.Collapsed;
    }

    private void ShowNameStep()
    {
        _onNameStep = true;
        var current = string.IsNullOrWhiteSpace(_user.DisplayName) ? _user.Username : _user.DisplayName;
        _stepTitle.Text = StepPrefix(_totalSteps) + "填写姓名";
        _stepNote.Text = $"当前显示为「{current}」。填写真实姓名后，欢迎语与工单操作员将显示您的名字；也可以以后再说。";
        if (_newPassword is not null) Group(_newPassword).Visibility = Visibility.Collapsed;
        if (_confirm is not null) Group(_confirm).Visibility = Visibility.Collapsed;
        Group(_name!).Visibility = Visibility.Visible;
        ConfirmButton.Content = "保存";
        if (CancelButton is not null) CancelButton.Content = "以后再说";
        _name!.Focus();
    }

    private void SavePassword()
    {
        if (PasswordPolicy.Validate(_user.Username, _newPassword!.Password) is { } problem)
        {
            ShowError(_newPassword, problem);
            return;
        }
        if (_newPassword.Password != _confirm!.Password)
        {
            ShowError(_confirm, "两次输入的新密码不一致");
            return;
        }
        try
        {
            _auth.ChangePassword(_user.Id, _currentPassword!, _newPassword.Password);
        }
        catch (Exception ex)
        {
            ShowError(_newPassword, ex.Message);
            return;
        }
        PasswordChanged = true;
        if (_askName) ShowNameStep();
        else Accept();
    }

    private void SaveName()
    {
        try
        {
            NamedUser = _auth.UpdateDisplayName(_user.Id, _name!.Text);
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_name!, ex.Message);
        }
    }

    private string StepPrefix(int step) => _totalSteps > 1 ? $"第 {step} 步 / 共 {_totalSteps} 步 · " : "";

    private static FrameworkElement Group(FrameworkElement input) => (FrameworkElement)input.Parent;
}
