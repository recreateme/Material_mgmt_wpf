using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Hardware;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>工单详情：基本信息、全部称重记录（含作废）、混料 / 涂布异常说明（可编辑保存）。</summary>
public sealed class WorkOrderDetailDialog : DialogWindow
{
    private readonly OrderService _orders;
    private readonly IAuditLog _audit;
    private readonly string _actor;
    private readonly int _orderId;
    private readonly CheckBox _mix;
    private readonly TextBox _mixNote;
    private readonly CheckBox _coat;
    private readonly TextBox _coatNote;

    public WorkOrderDetailDialog(OrderService orders, IAuditLog audit, string actor,
        int orderId, string formulaName)
        : base($"工单详情 #{orderId}", 980, "保存异常说明", "关闭")
    {
        _orders = orders;
        _audit = audit;
        _actor = actor;
        _orderId = orderId;
        MakeResizable(720, 720, 480);

        var wo = orders.GetDetail(orderId);

        Body.Children.Add(new TextBlock
        {
            Text = $"配方：{formulaName}\n操作员：{wo.Operator}\n生产编号：{wo.ProductionNo}\n" +
                   $"温度：{wo.Temperature}  湿度：{wo.Humidity}\n基膜：{wo.BaseFilm}  批次：{wo.BatchNo}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var records = wo.Records
            .OrderBy(x => x.FormulaItem?.Sequence ?? 0).ThenBy(x => x.Timestamp)
            .Select(DetailRecord.From)
            .ToList();
        var voidedCount = records.Count(x => !x.IsValid);
        Body.Children.Add(new TextBlock
        {
            Text = voidedCount > 0
                ? $"称重记录（{records.Count} 条，其中作废 {voidedCount} 条，作废记录不导出）"
                : $"称重记录（{records.Count} 条）",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var recordGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 300,
            Margin = new Thickness(0, 0, 0, 14),
            ItemsSource = records
        };
        recordGrid.Columns.Add(GridKit.Text("步骤", "Sequence", 50));
        recordGrid.Columns.Add(GridKit.Text("原料", "Material", 150, align: TextAlignment.Left));
        recordGrid.Columns.Add(GridKit.Text("实测(g)", "Actual", 80));
        recordGrid.Columns.Add(GridKit.Text("范围(g)", "Range", 110));
        recordGrid.Columns.Add(GridKit.Text("状态", "StateText", 60));
        recordGrid.Columns.Add(GridKit.Text("称重时间", "TimeText", 130));
        recordGrid.Columns.Add(GridKit.Text("扫码来源", "SourceText", 110));
        recordGrid.Columns.Add(GridKit.Text("作废说明", "VoidText",
            new DataGridLength(1, DataGridLengthUnitType.Star), align: TextAlignment.Left));
        recordGrid.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is DetailRecord { IsValid: false })
                e.Row.Foreground = UiTheme.Brush(UiTheme.Muted);
        };
        Body.Children.Add(recordGrid);

        _mix = new CheckBox { Content = "混料异常", IsChecked = wo.MixingException == true, Margin = new Thickness(0, 0, 0, 6) };
        Body.Children.Add(_mix);
        _mixNote = AddField("混料异常说明", NoteBox(wo.MixingExceptionNote, _mix.IsChecked == true));
        _coat = new CheckBox { Content = "涂布异常", IsChecked = wo.CoatingException == true, Margin = new Thickness(0, 0, 0, 6) };
        Body.Children.Add(_coat);
        _coatNote = AddField("涂布异常说明", NoteBox(wo.CoatingExceptionNote, _coat.IsChecked == true));
        _mix.Checked += (_, _) => _mixNote.IsEnabled = true;
        _mix.Unchecked += (_, _) => _mixNote.IsEnabled = false;
        _coat.Checked += (_, _) => _coatNote.IsEnabled = true;
        _coat.Unchecked += (_, _) => _coatNote.IsEnabled = false;
    }

    private static TextBox NoteBox(string? text, bool enabled) => new()
    {
        Height = 60,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalContentAlignment = VerticalAlignment.Top,
        Padding = new Thickness(10, 6, 10, 6),
        Text = text ?? "",
        IsEnabled = enabled
    };

    protected override void OnConfirm()
    {
        try
        {
            _orders.UpdateExceptionNotes(_orderId, _mix.IsChecked == true, _mixNote.Text,
                _coat.IsChecked == true, _coatNote.Text);
            _audit.Log("order", $"更新异常说明 工单#{_orderId}", _actor);
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_mixNote, $"保存失败：{ex.GetBaseException().Message}");
        }
    }

    /// <summary>详情中的一条称重记录：有效与作废都列出，限值 / 名称优先用称重时的快照。</summary>
    private sealed record DetailRecord(
        int Sequence, string Material, string Actual, string Range, bool IsValid,
        string TimeText, string SourceText, string VoidText)
    {
        public string StateText => IsValid ? "有效" : "作废";

        public static DetailRecord From(WeightRecord x)
        {
            var item = x.FormulaItem;
            var min = x.WeightMinSnapshot ?? item?.WeightMin;
            var max = x.WeightMaxSnapshot ?? item?.WeightMax;
            var voidText = x.IsValid
                ? ""
                : string.Join("  ", new[]
                {
                    x.VoidedAt?.ToString("MM-dd HH:mm"),
                    x.VoidedBy,
                    string.IsNullOrWhiteSpace(x.VoidReason) ? null : $"原因：{x.VoidReason}"
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return new DetailRecord(
                item?.Sequence ?? 0,
                x.MaterialNameSnapshot ?? item?.Material?.Name ?? "",
                x.ActualWeight.ToString("F3"),
                min is null || max is null ? "" : $"{min:0.###} ~ {max:0.###}",
                x.IsValid,
                x.Timestamp.ToString("MM-dd HH:mm:ss"),
                ScanSources.ToDisplay(x.ScanSource),
                voidText);
        }
    }
}
