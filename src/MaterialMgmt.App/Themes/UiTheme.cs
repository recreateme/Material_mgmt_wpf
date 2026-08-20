using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MaterialMgmt.App.Themes;

/// <summary>代码侧主题令牌与按钮工厂（样式定义在 AppTheme.xaml）。</summary>
public static class UiTheme
{
    public static readonly Color Primary = Color.FromRgb(0x25, 0x63, 0xEB);
    public static readonly Color NavBg = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color NavText = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color PageBg = Color.FromRgb(0xF8, 0xFA, 0xFC);
    public static readonly Color Border = Color.FromRgb(0xE2, 0xE8, 0xF0);
    public static readonly Color Text = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color Muted = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color Success = Color.FromRgb(0x16, 0xA3, 0x4A);
    public static readonly Color Warning = Color.FromRgb(0xD9, 0x77, 0x06);
    public static readonly Color Danger = Color.FromRgb(0xDC, 0x26, 0x26);
    public static readonly Color BannerStart = Color.FromRgb(0x1E, 0x3A, 0x5F);
    public static readonly Color BannerEnd = Color.FromRgb(0x25, 0x63, 0xEB);
    public static readonly Color WeightOk = Color.FromRgb(0x16, 0xA3, 0x4A);
    public static readonly Color WeightUnstable = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color WeightBad = Color.FromRgb(0xDC, 0x26, 0x26);

    public static SolidColorBrush Brush(Color c) => new(c);

    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 22,
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

    private static void TrySetStyle(FrameworkElement el, string key)
    {
        if (Application.Current?.TryFindResource(key) is Style style)
            el.Style = style;
    }
}
