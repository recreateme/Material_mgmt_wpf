using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>重称确认：选择作废原因（固定选项 + 备注，“其他”必须填写备注），确认后旧记录作废。</summary>
public sealed class ReworkReasonDialog : DialogWindow
{
    private readonly ComboBox _reason;
    private readonly TextBox _note;

    /// <summary>落库用的原因文本（原因或“原因：备注”）。</summary>
    public string Reason { get; private set; } = "";

    private ReworkReasonDialog(string stepText) : base("重新称量", 440, "作废并重称", "返回")
    {
        Body.Children.Add(new TextBlock
        {
            Text = $"重新称量{stepText}？\n该步原有效记录将作废（仍保留在历史详情中），其后已完成步骤不受影响。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.Brush(UiTheme.Text),
            Margin = new Thickness(0, 0, 0, 14)
        });
        _reason = AddField("作废原因 *", new ComboBox { ItemsSource = VoidReasons.Selectable, SelectedIndex = 0 });
        _note = AddField("备注", new TextBox { MaxLength = 200 }, "选择“其他”时必须填写");

        if (Application.Current?.TryFindResource("DangerButtonStyle") is Style danger)
            ConfirmButton.Style = danger;
    }

    protected override void OnConfirm()
    {
        var reason = _reason.SelectedItem as string ?? VoidReasons.Other;
        if (reason == VoidReasons.Other && string.IsNullOrWhiteSpace(_note.Text))
        {
            ShowError(_note, "请填写具体原因");
            return;
        }
        Reason = VoidReasons.Compose(reason, _note.Text);
        Accept();
    }

    /// <summary>返回作废原因；用户取消时返回 null。</summary>
    public static string? Ask(string stepText)
    {
        var dlg = new ReworkReasonDialog(stepText);
        return dlg.ShowDialog() == true ? dlg.Reason : null;
    }
}
