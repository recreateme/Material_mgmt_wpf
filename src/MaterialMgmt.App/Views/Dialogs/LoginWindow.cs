using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>
/// 登录窗：左侧为按比例完整缩放的背景图（不裁切），右侧独立深蓝区域放登录卡片，互不遮挡。
/// 记住上次用户名（不存密码）；连续输错锁定时倒计时每秒刷新，到时自动恢复。
/// </summary>
public partial class LoginWindow : Window
{
    private const double PanelWidth = 380;

    private readonly IAuthService _auth;
    private readonly IConfigService _config;
    private readonly IAuditLog? _audit;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private TextBox _user = null!;
    private PasswordBox _pass = null!;
    private TextBox _plainPass = null!;
    private Button _reveal = null!;
    private TextBlock _caps = null!;
    private TextBlock _error = null!;
    private Button _login = null!;

    public CurrentUser? LoggedInUser { get; private set; }

    public LoginWindow(IAuthService auth, IConfigService config, IAuditLog? audit = null)
    {
        _auth = auth;
        _config = config;
        _audit = audit;
        Title = "原料管理系统 — 登录";
        WindowSizing.Fit(this, 1240, 480, 900, 420);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.Brush(UiTheme.LoginBackdrop);
        BuildUi();

        _lockTimer.Tick += (_, _) => RefreshLockout();
        Closed += (_, _) => _lockTimer.Stop();
        PreviewKeyUp += (_, _) => UpdateCapsHint();
    }

    private void BuildUi()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelWidth) });

        var art = BuildArtArea();
        Grid.SetColumn(art, 0);
        root.Children.Add(art);

        var side = new Border
        {
            Background = new LinearGradientBrush(UiTheme.LoginPanel, UiTheme.LoginBackdrop, 90),
            Child = BuildCard()
        };
        Grid.SetColumn(side, 1);
        root.Children.Add(side);

        Content = root;
        Loaded += (_, _) =>
        {
            if (_user.Text.Length > 0) _pass.Focus();
            else _user.Focus();
        };
    }

    /// <summary>背景图区：图片未就绪（或加载失败）时显示渐变与系统名称，图片就绪后只显示图片。</summary>
    private Grid BuildArtArea()
    {
        var area = new Grid { ClipToBounds = true };
        var fallback = new Border
        {
            Background = new LinearGradientBrush(UiTheme.LoginBackdrop, UiTheme.BannerStart, 45),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "原料管理系统", FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                        Text = "实验室称量 · 配方 · 工单追溯", FontSize = 15, Margin = new Thickness(2, 10, 0, 0),
                        Foreground = UiTheme.Brush(UiTheme.WithAlpha(Colors.White, 0xCC))
                    }
                }
            }
        };
        var photo = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.HighQuality);
        area.Children.Add(fallback);
        area.Children.Add(photo);

        LoadBackgroundImage(img =>
        {
            photo.Source = img;
            photo.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        });
        return area;
    }

    private Border BuildCard()
    {
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
            Text = $"实验室称量 · WPF {AppInfo.DisplayVersion}",
            FontSize = UiTheme.FontCaption,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 20)
        });

        stack.Children.Add(UiTheme.FieldLabel("用户名"));
        _user = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 14),
            Text = _config.GetValue("app", "last_username", "")
        };
        _user.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || _pass.Password.Length > 0) return;
            e.Handled = true;
            FocusPassword();
        };
        _user.TextChanged += (_, _) => RefreshLockout();
        stack.Children.Add(_user);

        stack.Children.Add(UiTheme.FieldLabel("密码"));
        stack.Children.Add(BuildPasswordRow());

        _caps = new TextBlock
        {
            Text = "大写锁定已开启",
            FontSize = UiTheme.FontCaption,
            Foreground = UiTheme.Brush(UiTheme.Warning),
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(_caps);

        _error = new TextBlock
        {
            Foreground = UiTheme.Brush(UiTheme.Danger),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
            FontSize = UiTheme.FontCaption
        };
        stack.Children.Add(_error);

        _login = UiTheme.PrimaryButton("登 录");
        _login.Height = 40;
        _login.Margin = new Thickness(0, 16, 0, 0);
        _login.HorizontalAlignment = HorizontalAlignment.Stretch;
        _login.IsDefault = true;
        _login.Click += (_, _) => DoLogin();
        stack.Children.Add(_login);

        return new Border
        {
            Margin = new Thickness(32, 24, 32, 24),
            Padding = new Thickness(28),
            Background = UiTheme.Brush(Colors.White),
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black
            },
            Child = stack
        };
    }

    /// <summary>密码框 + 显示/隐藏切换。明文框与密码框双向同步，登录始终读密码框。</summary>
    private Grid BuildPasswordRow()
    {
        _pass = new PasswordBox();
        _plainPass = new TextBox { Visibility = Visibility.Collapsed };
        _plainPass.TextChanged += (_, _) =>
        {
            if (_plainPass.IsVisible) _pass.Password = _plainPass.Text;
        };
        foreach (var box in new Control[] { _pass, _plainPass })
        {
            box.GotKeyboardFocus += (_, _) => UpdateCapsHint();
            box.LostKeyboardFocus += (_, _) => UpdateCapsHint();
        }

        _reveal = UiTheme.GhostButton("");
        _reveal.Content = EyeGlyph(RevealGlyph);
        _reveal.Width = 36;
        _reveal.Padding = new Thickness(0);
        _reveal.Margin = new Thickness(6, 0, 0, 0);
        _reveal.Focusable = false;
        _reveal.ToolTip = "显示密码";
        _reveal.Click += (_, _) => TogglePasswordVisibility();

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_pass);
        row.Children.Add(_plainPass);
        Grid.SetColumn(_reveal, 1);
        row.Children.Add(_reveal);
        return row;
    }

    private void TogglePasswordVisibility()
    {
        var show = _plainPass.Visibility != Visibility.Visible;
        if (show)
        {
            _plainPass.Text = _pass.Password;
            _plainPass.Visibility = Visibility.Visible;
            _pass.Visibility = Visibility.Collapsed;
            _reveal.Content = EyeGlyph(HideGlyph);
            _reveal.ToolTip = "隐藏密码";
        }
        else
        {
            _pass.Password = _plainPass.Text;
            _pass.Visibility = Visibility.Visible;
            _plainPass.Visibility = Visibility.Collapsed;
            _plainPass.Text = "";
            _reveal.Content = EyeGlyph(RevealGlyph);
            _reveal.ToolTip = "显示密码";
        }
        FocusPassword();
    }

    private const string RevealGlyph = "\uE7B3";
    private const string HideGlyph = "\uED1A";

    private static TextBlock EyeGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = NavMenu.IconFont,
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private void FocusPassword()
    {
        if (_plainPass.IsVisible)
        {
            _plainPass.Focus();
            _plainPass.CaretIndex = _plainPass.Text.Length;
        }
        else
        {
            _pass.Focus();
        }
    }

    private void UpdateCapsHint()
    {
        var inPassword = _pass.IsKeyboardFocused || _plainPass.IsKeyboardFocused;
        _caps.Visibility = inPassword && Keyboard.IsKeyToggled(Key.CapsLock)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>当前用户名处于锁定时每秒刷新剩余秒数并禁用登录；到时自动恢复。</summary>
    private void RefreshLockout()
    {
        if (_auth.GetLockoutRemaining(_user.Text.Trim()) is { } left)
        {
            ShowError($"登录失败次数过多，请 {Math.Ceiling(left.TotalSeconds)} 秒后再试");
            _login.IsEnabled = false;
            if (!_lockTimer.IsEnabled) _lockTimer.Start();
            return;
        }
        if (!_lockTimer.IsEnabled) return;
        _lockTimer.Stop();
        _login.IsEnabled = true;
        _error.Visibility = Visibility.Collapsed;
    }

    /// <summary>默认只用随包的 Assets\login-bg.jpg；只有 [app] login_bg_online = true 时才尝试在线图（5 秒超时）。</summary>
    private const string RemoteBackgroundUrl =
        "https://omo-oss-image.thefastimg.com/portal-saas/new2022061611535418279/cms/image/e4ecf8a7-b93e-4b95-befd-d7d5c13db784.jpg";

    private async void LoadBackgroundImage(Action<BitmapSource> onReady)
    {
        try
        {
            foreach (var name in new[] { "login-bg.jpg", "login-bg.png" })
            {
                var local = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                if (!File.Exists(local)) continue;
                onReady(Decode(File.ReadAllBytes(local)));
                return;
            }

            if (!bool.TryParse(_config.GetValue("app", "login_bg_online", "false"), out var online) || !online)
                return;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var bytes = await http.GetByteArrayAsync(RemoteBackgroundUrl);
            onReady(Decode(bytes));
        }
        catch
        {
            // 背景图只是装饰，任何失败都保留渐变
        }
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void DoLogin()
    {
        if (_plainPass.IsVisible) _pass.Password = _plainPass.Text;
        var username = _user.Text.Trim();
        var password = _pass.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请输入用户名和密码");
            return;
        }

        if (_auth.GetLockoutRemaining(username) is not null)
        {
            RefreshLockout();
            Shake();
            return;
        }

        var user = _auth.Login(username, password);
        if (user is null)
        {
            _audit?.Log("auth", $"登录失败 username={username}");
            if (_auth.GetLockoutRemaining(username) is not null) RefreshLockout();
            else ShowError("用户名或密码错误，或账号已禁用");
            Shake();
            return;
        }

        var mustChangePassword = _auth.IsDefaultPassword(username, password);
        if (mustChangePassword || user.HasPlaceholderName)
        {
            var wizard = new FirstLoginWizard(_auth, user, mustChangePassword ? password : null, user.HasPlaceholderName)
            {
                Owner = this
            };
            wizard.ShowDialog();
            if (mustChangePassword && !wizard.PasswordChanged)
            {
                _auth.Logout();
                _audit?.Log("auth", "未修改默认密码，登录取消", user.Username);
                ShowError("必须修改默认密码后才能登录");
                return;
            }
            if (wizard.PasswordChanged) _audit?.Log("auth", "已修改默认密码", user.Username);
            if (wizard.NamedUser is { } named)
            {
                user = named;
                _audit?.Log("auth", $"已设置姓名：{named.DisplayName}", named.Username);
            }
        }

        try
        {
            _config.SetValue("app", "last_username", user.Username);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 记住用户名只是便利，配置文件被占用时忽略
        }
        _audit?.Log("auth", $"登录成功 role={user.Role}", user.Username);
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
