using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MaterialMgmt.App.Themes;

/// <summary>代码侧主题令牌与按钮工厂（样式定义在 AppTheme.xaml）。</summary>
public static class UiTheme
{
    // 颜色与字号的唯一定义在 Themes/AppTheme.xaml；这里只是读取资源，缺失时返回醒目的洋红色便于发现。
    public static readonly Color Primary = Res("C.Primary");
    public static readonly Color PrimarySoft = Res("C.PrimarySoft");
    public static readonly Color NavBg = Res("C.NavBg");
    public static readonly Color NavText = Res("C.NavText");
    public static readonly Color NavTitle = Res("C.NavTitle");
    public static readonly Color NavUser = Res("C.NavUser");
    public static readonly Color PageBg = Res("C.PageBg");
    public static readonly Color PanelAlt = Res("C.PanelAlt");
    public static readonly Color Border = Res("C.Border");
    public static readonly Color Text = Res("C.Text");
    public static readonly Color Muted = Res("C.Muted");
    public static readonly Color Success = Res("C.Success");
    public static readonly Color SuccessSoft = Res("C.SuccessSoft");
    public static readonly Color Warning = Res("C.Warning");
    public static readonly Color WarningSoft = Res("C.WarningSoft");
    public static readonly Color Danger = Res("C.Danger");
    public static readonly Color DangerSoft = Res("C.DangerSoft");
    public static readonly Color BannerStart = Res("C.BannerStart");
    public static readonly Color BannerEnd = Res("C.BannerEnd");
    public static readonly Color WeightOk = Res("C.Success");
    public static readonly Color WeightUnstable = Res("C.WeightUnstable");
    public static readonly Color WeightBad = Res("C.Danger");
    public static readonly Color LoginBackdrop = Res("C.LoginBackdrop");
    public static readonly Color LoginPanel = Res("C.LoginPanel");

    /// <summary>4 档字号：标题 / 小标题 / 正文 / 说明（最小 12）。</summary>
    public static readonly double FontTitle = ResDouble("FontSizeTitle", 20);
    public static readonly double FontSubtitle = ResDouble("FontSizeSubtitle", 15);
    public static readonly double FontBody = ResDouble("FontSizeBody", 14);
    public static readonly double FontCaption = ResDouble("FontSizeCaption", 12);

    private static Color Res(string key)
        => Application.Current?.TryFindResource(key) is Color c ? c : Colors.Magenta;

    private static double ResDouble(string key, double fallback)
        => Application.Current?.TryFindResource(key) is double d ? d : fallback;

    /// <summary>给颜色加透明度（用于遮罩、半透明文字），避免在面板里手写 ARGB。</summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static SolidColorBrush Brush(Color c) => new(c);

    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = FontTitle,
        FontWeight = FontWeights.Bold,
        Foreground = Brush(Text),
        Margin = new Thickness(0, 0, 16, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Button PrimaryButton(string text)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        TrySetStyle(b, "PrimaryButtonStyle");
        return b;
    }

    public static Button DangerButton(string text)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        TrySetStyle(b, "DangerButtonStyle");
        return b;
    }

    public static Button GhostButton(string text)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        TrySetStyle(b, "GhostButtonStyle");
        return b;
    }

    public static Button NavFooterButton(string text, bool logout = false)
    {
        var b = new Button { Content = text };
        TrySetStyle(b, logout ? "NavLogoutButtonStyle" : "NavFooterButtonStyle");
        return b;
    }

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush(Border),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = padding ?? new Thickness(16),
        Child = child
    };

    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(Muted),
        Margin = new Thickness(0, 0, 0, 4)
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush(Muted),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };

    public static TextBlock Breadcrumb(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush(Muted),
        Margin = new Thickness(0, 0, 0, 8)
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = FontSubtitle,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(Text),
        Margin = new Thickness(0, 12, 0, 6)
    };

    public static TextBlock StatusBadge(string text, Color color) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(color),
        Padding = new Thickness(8, 3, 8, 3),
        Background = new SolidColorBrush(WithAlpha(color, 0x22)),
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public static Color StatusColor(string status) => status switch
    {
        "in_progress" => Warning,
        "completed" => Success,
        "cancelled" => Muted,
        _ => Text
    };

    public static string StatusLabel(string status) => status switch
    {
        "in_progress" => "进行中",
        "completed" => "已完成",
        "cancelled" => "已取消",
        _ => status
    };

    /// <summary>长内容弹窗：上方可滚，底栏固定按钮区。</summary>
    public static DockPanel DialogBody(UIElement formContent, UIElement footerButtons)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(footerButtons, Dock.Bottom);
        dock.Children.Add(footerButtons);
        dock.Children.Add(new ScrollViewer
        {
            Content = formContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return dock;
    }

    public static StackPanel DialogFooterButtons(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 8, 20, 16)
        };
        foreach (var b in buttons)
            row.Children.Add(b);
        return row;
    }

    private static void TrySetStyle(FrameworkElement el, string key)
    {
        if (Application.Current?.TryFindResource(key) is Style style)
            el.Style = style;
    }
}
