using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>统一的确认框：删除、取消工单、作废记录等破坏性操作用红色确认按钮；默认焦点在“取消”，避免误按 Enter。</summary>
public sealed class ConfirmDialog : DialogWindow
{
    private ConfirmDialog(string title, string message, string confirmText, string cancelText, bool danger)
        : base(title, 420, confirmText, cancelText)
    {
        Body.Margin = new Thickness(24, 22, 24, 12);
        Body.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = UiTheme.FontBody,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Text)
        });

        if (danger && Application.Current?.TryFindResource("DangerButtonStyle") is Style dangerStyle)
            ConfirmButton.Style = dangerStyle;

        // Enter 不应直接触发破坏性操作：确认按钮不再是默认按钮，焦点放在取消按钮上
        ConfirmButton.IsDefault = false;
        Loaded += (_, _) => (CancelButton ?? ConfirmButton).Focus();
    }

    protected override void OnConfirm() => Accept();

    /// <summary>显示确认框，返回 true 表示用户确认。</summary>
    public static bool Ask(string message, string title = "确认", string confirmText = "确定",
        string cancelText = "取消", bool danger = false)
    {
        var dlg = new ConfirmDialog(title, message, confirmText, cancelText, danger);
        return dlg.ShowDialog() == true;
    }
}
