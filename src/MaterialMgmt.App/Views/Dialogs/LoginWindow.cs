using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;

namespace MaterialMgmt.App.Views.Dialogs;

public partial class LoginWindow : Window
{
    private readonly IAuthService _auth;
    private readonly IAuditLog? _audit;

    public CurrentUser? LoggedInUser { get; private set; }

    public LoginWindow(IAuthService auth, IAuditLog? audit = null)
    {
        _auth = auth;
        _audit = audit;
        Title = "原料管理系统 — 登录";
        WindowSizing.Fit(this, 960, 580, 760, 440);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        BuildUi();
    }

    private TextBox _user = null!;
    private PasswordBox _pass = null!;
    private TextBlock _error = null!;

    private void BuildUi()
    {
        var root = new Grid();

        // 底层渐变（图片未就绪 / 加载失败时的回退）
        root.Children.Add(new Border { Background = CreateBackdropBrush() });
        var grid = new Border { Background = CreateGridPatternBrush(), Opacity = 0.22 };
        var hatch = new Border { Background = CreateDiagonalHatchBrush(), Opacity = 0.12 };
        root.Children.Add(grid);
        root.Children.Add(hatch);

        // 背景图：加载成功后盖住纹理，并叠一层深色渐变保证文字与卡片可读
        var photo = new Border { Visibility = Visibility.Collapsed };
        var tint = new Border { Visibility = Visibility.Collapsed, Background = CreateTintBrush() };
        root.Children.Add(photo);
        root.Children.Add(tint);
        LoadBackgroundImage(photo, () =>
        {
            photo.Visibility = Visibility.Visible;
            tint.Visibility = Visibility.Visible;
            grid.Visibility = Visibility.Collapsed;
            hatch.Visibility = Visibility.Collapsed;
        });

        // 左侧品牌文案
        root.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(72, 0, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "原料管理系统", FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.6 }
                },
                new TextBlock
                {
                    Text = "实验室称量 · 配方 · 工单追溯", FontSize = 16, Margin = new Thickness(2, 10, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.6 }
                }
            }
        });

        var card = new Border
        {
            Width = 360,
            Margin = new Thickness(0, 16, 72, 16),
            Padding = new Thickness(28, 28, 28, 28),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xF8, 0xFA, 0xFC)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black
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
            Text = $"实验室称量 · WPF {AppInfo.DisplayVersion}",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 22)
        });

        stack.Children.Add(UiTheme.Hint("输入账号密码登录；权限由账号角色决定"));

        stack.Children.Add(UiTheme.FieldLabel("用户名"));
        _user = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(_user);

        stack.Children.Add(UiTheme.FieldLabel("密码"));
        _pass = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
        _pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) DoLogin(); };
        stack.Children.Add(_pass);

        _error = new TextBlock
        {
            Foreground = UiTheme.Brush(UiTheme.Danger),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 18,
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

        card.Child = stack;
        root.Children.Add(card);
        Content = root;
        Loaded += (_, _) => _user.Focus();
    }

    /// <summary>登录背景图（CMP 金刚石磨盘）。在线地址来自公司官网；内网/离线机器请把图片放到 Assets\login-bg.jpg。</summary>
    private const string RemoteBackgroundUrl =
        "https://omo-oss-image.thefastimg.com/portal-saas/new2022061611535418279/cms/image/e4ecf8a7-b93e-4b95-befd-d7d5c13db784.jpg";

    /// <summary>优先本地文件，其次在线图片（异步下载，失败自动回退渐变），不阻塞登录界面。</summary>
    private static void LoadBackgroundImage(Border target, Action onReady)
    {
        try
        {
            foreach (var name in new[] { "login-bg.jpg", "login-bg.png" })
            {
                var local = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                if (!File.Exists(local)) continue;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(local, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                Apply(bmp);
                return;
            }

            var remote = new BitmapImage();
            remote.BeginInit();
            remote.UriSource = new Uri(RemoteBackgroundUrl, UriKind.Absolute);
            remote.CacheOption = BitmapCacheOption.OnLoad;
            remote.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            remote.DecodePixelWidth = 1920;
            remote.EndInit();
            if (remote.IsDownloading)
                remote.DownloadCompleted += (_, _) => Apply(remote);
            else
                Apply(remote);
            // DownloadFailed / DecodeFailed：保持渐变背景，无需处理
        }
        catch
        {
            // 背景图只是装饰，任何失败都回退到渐变
        }

        void Apply(BitmapImage img)
        {
            target.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            onReady();
        }
    }

    private static LinearGradientBrush CreateTintBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xB3, 0x0B, 0x12, 0x20), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x0B, 0x12, 0x20), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0x0B, 0x12, 0x20), 1));
        return b;
    }

    private static LinearGradientBrush CreateBackdropBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x17, 0x2A), 0));
        brush.GradientStops.Add(new GradientStop(UiTheme.BannerStart, 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1E, 0x3A, 0x5F), 1));
        return brush;
    }

    private static DrawingBrush CreateGridPatternBrush()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x94, 0xA3, 0xB8)), 1);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 24))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(24, 0))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private static DrawingBrush CreateDiagonalHatchBrush()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xE2, 0xE8, 0xF0)), 1.2);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 32), new Point(32, 0))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 32, 32),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private void DoLogin()
    {
        var username = _user.Text.Trim();
        var password = _pass.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请输入用户名和密码");
            return;
        }

        if (_auth.GetLockoutRemaining(username) is { } wait)
        {
            ShowError($"登录失败次数过多，请 {Math.Ceiling(wait.TotalSeconds)} 秒后再试");
            Shake();
            return;
        }

        var user = _auth.Login(username, password);
        if (user is null)
        {
            _audit?.Log("auth", $"登录失败 username={username}");
            ShowError(_auth.GetLockoutRemaining(username) is { } w
                ? $"登录失败次数过多，账号已锁定 {Math.Ceiling(w.TotalSeconds)} 秒"
                : "用户名或密码错误，或账号已禁用");
            Shake();
            return;
        }

        // 仍是出厂默认密码：必须先改密才能进入系统
        if (_auth.IsDefaultPassword(username, password))
        {
            MessageBox.Show(this, "当前仍在使用出厂默认密码，请先修改密码后再进入系统。", "安全提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            var dlg = new ChangePasswordDialog(_auth, user.Id) { Owner = this };
            if (dlg.ShowDialog() != true)
            {
                _auth.Logout();
                _audit?.Log("auth", "未修改默认密码，登录取消", user.Username);
                ShowError("必须修改默认密码后才能登录");
                return;
            }
            _audit?.Log("auth", "已修改默认密码", user.Username);
        }

        // 显示名只是“业务人员/系统管理员”之类的角色称呼时，请用户填写姓名，欢迎语与工单记录才能落到具体的人
        if (user.HasPlaceholderName)
        {
            var nameDlg = new DisplayNameDialog(_auth, user) { Owner = this };
            if (nameDlg.ShowDialog() == true && nameDlg.UpdatedUser is { } named)
            {
                user = named;
                _audit?.Log("auth", $"已设置姓名：{named.DisplayName}", named.Username);
            }
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
