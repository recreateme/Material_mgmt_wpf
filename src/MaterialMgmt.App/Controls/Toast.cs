using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Controls;

/// <summary>
/// 轻提示：非模态、出现在当前主窗口右上角、约 3 秒后淡出，不抢焦点、不打断称量。
/// 成功 / 警告 / 错误三种；多条同时出现时自上而下排列。
/// </summary>
public static class Toast
{
    private static readonly List<ToastWindow> Active = new();

    public static void Success(string message) => Show(message, UiTheme.Success, "✓");

    /// <summary>带一个操作按钮（如“打开所在文件夹”）的成功提示；停留更久，鼠标悬停时不消失。</summary>
    public static void Success(string message, string actionText, Action action)
        => Show(message, UiTheme.Success, "✓", seconds: 8, action: (actionText, action));

    public static void Warning(string message) => Show(message, UiTheme.Warning, "!");

    public static void Error(string message) => Show(message, UiTheme.Danger, "✕", seconds: 5);

    private static void Show(string message, Color accent, string icon, double seconds = 3,
        (string Text, Action Run)? action = null)
    {
        var app = Application.Current;
        if (app is null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => Show(message, accent, icon, seconds, action));
            return;
        }

        var owner = FindRootOwner();
        if (owner is null) return;

        var toast = new ToastWindow(owner, message, accent, icon, action);
        Active.Add(toast);
        toast.Closed += (_, _) =>
        {
            Active.Remove(toast);
            Reflow(owner);
        };
        toast.Loaded += (_, _) => Reflow(owner);
        toast.Show();
        toast.BeginAutoClose(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>从当前激活窗口沿 Owner 链找到顶层窗口（主窗 / 称量窗），避免挂在即将关闭的对话框上。</summary>
    private static Window? FindRootOwner()
    {
        var windows = Application.Current.Windows.OfType<Window>()
            .Where(x => x.IsVisible && x is not ToastWindow)
            .ToList();
        var w = windows.FirstOrDefault(x => x.IsActive)
                ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : windows.FirstOrDefault());
        while (w?.Owner is { IsVisible: true } parent && parent is not ToastWindow)
            w = parent;
        return w;
    }

    private static void Reflow(Window owner)
    {
        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget is null) return;
        var toDip = source.CompositionTarget.TransformFromDevice;

        // 右上角（设备像素）→ DIP；最大化时同样适用
        var topRight = toDip.Transform(owner.PointToScreen(new Point(owner.ActualWidth, 0)));
        var y = topRight.Y + 16;
        foreach (var t in Active.Where(t => ReferenceEquals(t.Owner, owner)).ToList())
        {
            if (!t.IsLoaded) continue;
            t.Left = topRight.X - t.ActualWidth - 20;
            t.Top = y;
            y += t.ActualHeight + 8;
        }
    }
}

internal sealed class ToastWindow : Window
{
    public ToastWindow(Window owner, string message, Color accent, string icon, (string Text, Action Run)? action = null)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MaxWidth = 420;
        // 先放到屏幕外，等布局完成后由 Toast.Reflow 移到主窗右上角，避免左上角闪一下
        Left = -10000;
        Top = -10000;

        var badge = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = UiTheme.Brush(accent), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = icon, Foreground = Brushes.White, FontSize = UiTheme.FontCaption, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var text = new TextBlock
        {
            Text = message, FontSize = UiTheme.FontBody, TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Text), Margin = new Thickness(10, 1, 0, 0), MaxWidth = 340
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(badge);
        if (action is { } a)
        {
            var link = new Button
            {
                Content = a.Text,
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = false,
                Margin = new Thickness(10, 6, 0, 0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = UiTheme.Brush(UiTheme.Primary),
                FontSize = UiTheme.FontCaption,
                Template = LinkTemplate()
            };
            link.Click += (_, _) =>
            {
                a.Run();
                Close();
            };
            row.Children.Add(new StackPanel { Children = { text, link } });
        }
        else
        {
            row.Children.Add(text);
        }

        Content = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(14, 12, 18, 12),
            Background = Brushes.White,
            BorderBrush = UiTheme.Brush(UiTheme.WithAlpha(accent, 0x99)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14, ShadowDepth = 2, Opacity = 0.22, Color = Colors.Black
            },
            Child = row
        };
    }

    /// <summary>只显示内容、带下划线的文字按钮。</summary>
    private static ControlTemplate LinkTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        text.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = text };
    }

    public void BeginAutoClose(TimeSpan visible)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = visible };
        Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            if (IsMouseOver) return;
            timer.Stop();
            if (!IsVisible) return;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                if (IsVisible) Close();
            };
            BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }
}
