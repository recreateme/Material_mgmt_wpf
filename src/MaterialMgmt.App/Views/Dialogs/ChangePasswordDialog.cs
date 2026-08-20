using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;

namespace MaterialMgmt.App.Views.Dialogs;

public sealed class ChangePasswordDialog : Window
{
    private readonly IAuthService _auth;
    private readonly int _userId;
    private readonly PasswordBox _old = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10) };
    private readonly PasswordBox _nw = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10) };
    private readonly PasswordBox _confirm = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10) };

    public ChangePasswordDialog(IAuthService auth, int userId)
    {
        _auth = auth;
        _userId = userId;
        Title = "修改密码";
        Width = 380;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = "原密码" });
        stack.Children.Add(_old);
        stack.Children.Add(new TextBlock { Text = "新密码（至少 6 位）" });
        stack.Children.Add(_nw);
        stack.Children.Add(new TextBlock { Text = "确认新密码" });
        stack.Children.Add(_confirm);

        var btn = UiTheme.PrimaryButton("保存");
        btn.Height = 34;
        btn.Margin = new Thickness(0, 12, 0, 0);
        btn.Click += (_, _) => Submit();
        stack.Children.Add(btn);
        Content = stack;
    }

    private void Submit()
    {
        if (_nw.Password != _confirm.Password)
        {
            MessageBox.Show(this, "两次输入的新密码不一致", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            _auth.ChangePassword(_userId, _old.Password, _nw.Password);
            MessageBox.Show(this, "密码已修改", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
