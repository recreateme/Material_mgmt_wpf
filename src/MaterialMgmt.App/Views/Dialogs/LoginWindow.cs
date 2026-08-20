using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

public partial class LoginWindow : Window
{
    private readonly IAuthService _auth;

    public CurrentUser? LoggedInUser { get; private set; }

    public LoginWindow(IAuthService auth)
    {
        _auth = auth;
        Title = "原料管理系统 — 登录";
        Width = 440;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new LinearGradientBrush(UiTheme.BannerStart, Color.FromRgb(0x0F, 0x17, 0x2A), 90);
        BuildUi();
    }

    private ComboBox _portal = null!;
    private TextBox _user = null!;
    private PasswordBox _pass = null!;
    private TextBlock _error = null!;

    private void BuildUi()
    {
        var card = new Border
        {
            Width = 360,
            Margin = new Thickness(0, 40, 0, 40),
            Padding = new Thickness(28, 28, 28, 24),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25, Color = Colors.Black
            }
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "原料管理系统",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.BannerStart),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = "实验室称量 · WPF v4.0",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 22)
        });

        stack.Children.Add(FieldLabel("登录入口"));
        _portal = new ComboBox { Margin = new Thickness(0, 4, 0, 14), Height = 34 };
        _portal.Items.Add("管理员");
        _portal.Items.Add("操作员");
        _portal.SelectedIndex = 0;
        stack.Children.Add(_portal);

        stack.Children.Add(FieldLabel("用户名"));
        _user = new TextBox { Margin = new Thickness(0, 4, 0, 14), Height = 34 };
        stack.Children.Add(_user);

        stack.Children.Add(FieldLabel("密码"));
        _pass = new PasswordBox { Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        _pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) DoLogin(); };
        stack.Children.Add(_pass);

        _error = new TextBlock
        {
            Foreground = UiTheme.Brush(UiTheme.Danger),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
            FontSize = 12
        };
        stack.Children.Add(_error);

        var btn = UiTheme.PrimaryButton("登 录");
        btn.Height = 40;
        btn.Margin = new Thickness(0, 10, 0, 0);
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.Click += (_, _) => DoLogin();
        stack.Children.Add(btn);

        stack.Children.Add(new TextBlock
        {
            Text = "root 账号可任选入口登录",
            FontSize = 11,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        });

        card.Child = stack;
        Content = card;
        Loaded += (_, _) => _user.Focus();
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.Brush(UiTheme.Muted)
    };

    private string SelectedPortalRole() =>
        _portal.SelectedIndex == 0 ? Roles.Admin : Roles.Operator;

    private void DoLogin()
    {
        var username = _user.Text.Trim();
        var password = _pass.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请输入用户名和密码");
            return;
        }

        var user = _auth.Login(username, password);
        if (user is null)
        {
            ShowError("用户名或密码错误，或账号已禁用");
            Shake();
            return;
        }

        if (!user.IsRoot() && user.Role != SelectedPortalRole())
        {
            _auth.Logout();
            ShowError(user.Role == Roles.Admin
                ? "该账号为管理员，请选择「管理员」入口"
                : "该账号为操作员，请选择「操作员」入口");
            Shake();
            return;
        }

        LoggedInUser = user;
        DialogResult = true;
        Close();
    }

    private void ShowError(string msg)
    {
        _error.Text = msg;
        _error.Visibility = Visibility.Visible;
    }

    private void Shake()
    {
        var tt = new TranslateTransform();
        RenderTransform = tt;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
