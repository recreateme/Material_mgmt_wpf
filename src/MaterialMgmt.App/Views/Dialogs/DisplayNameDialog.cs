using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>首次登录时填写姓名（显示名），用于欢迎语、工单操作员与配方创建人。可跳过。</summary>
public sealed class DisplayNameDialog : Window
{
    private readonly IAuthService _auth;
    private readonly CurrentUser _user;
    private readonly TextBox _name = new() { Height = 32, Margin = new Thickness(0, 4, 0, 10), MaxLength = 64 };

    public CurrentUser? UpdatedUser { get; private set; }

    public DisplayNameDialog(IAuthService auth, CurrentUser user)
    {
        _auth = auth;
        _user = user;
        Title = "请填写您的姓名";
        Width = 400;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = $"当前显示为「{(string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName)}」。\n" +
                   "请填写真实姓名，登录欢迎语与工单操作员将显示您的名字。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new TextBlock { Text = "姓名" });
        stack.Children.Add(_name);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var skip = UiTheme.GhostButton("以后再说");
        skip.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = UiTheme.PrimaryButton("保存");
        ok.Height = 34;
        ok.Margin = new Thickness(8, 0, 0, 0);
        ok.Click += (_, _) => Submit();
        row.Children.Add(skip);
        row.Children.Add(ok);
        stack.Children.Add(row);

        Content = stack;
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        Loaded += (_, _) => _name.Focus();
    }

    private void Submit()
    {
        try
        {
            UpdatedUser = _auth.UpdateDisplayName(_user.Id, _name.Text);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
