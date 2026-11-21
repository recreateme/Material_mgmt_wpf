using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Views.Dialogs;

public enum MessageKind
{
    Info,
    Warning,
    Error
}

/// <summary>替代系统 MessageBox 的提示窗：跟随当前窗口居中、使用主题样式，Enter / Esc 均可关闭。</summary>
public sealed class MessageDialog : DialogWindow
{
    private MessageDialog(string title, string message, MessageKind kind) : base(title, 440, "知道了", null)
    {
        ConfirmButton.IsCancel = true;
        Body.Margin = new Thickness(24, 22, 24, 12);

        var (accent, icon) = kind switch
        {
            MessageKind.Error => (UiTheme.Danger, "✕"),
            MessageKind.Warning => (UiTheme.Warning, "!"),
            _ => (UiTheme.Primary, "i")
        };
        var badge = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = UiTheme.Brush(accent),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = icon, Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                FontSize = UiTheme.FontBody,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var text = new TextBlock
        {
            Text = message,
            FontSize = UiTheme.FontBody,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Text),
            Margin = new Thickness(12, 3, 0, 0)
        };
        var row = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(text);
        Body.Children.Add(row);
        Loaded += (_, _) => ConfirmButton.Focus();
    }

    protected override void OnConfirm() => Accept();

    public static void Show(string message, string title, MessageKind kind)
        => new MessageDialog(title, message, kind).ShowDialog();
}
