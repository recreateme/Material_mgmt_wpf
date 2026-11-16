using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;

namespace MaterialMgmt.App.Views.Dialogs;

public sealed class WorkOrderParams
{
    public double Temperature { get; init; }
    public double Humidity { get; init; }
    public string ProductionNo { get; init; } = "";
    public double? D50 { get; init; }
    public bool MixingException { get; init; }
    public string? MixingExceptionNote { get; init; }
    public string BaseFilm { get; init; } = "";
    public string? BatchNo { get; init; }
    public double? Thickness { get; init; }
    public bool CoatingException { get; init; }
    public string? CoatingExceptionNote { get; init; }
}

public sealed class WorkOrderParamsDialog : Window
{
    public WorkOrderParams? Params { get; private set; }

    private readonly TextBox _temp = new() { Text = "25.0" };
    private readonly TextBox _hum = new() { Text = "50.0" };
    private readonly TextBox _prod = new();
    private readonly TextBox _d50 = new();
    private readonly CheckBox _mix = new() { Content = "混料工序有异常", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _mixNote = new() { MinHeight = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _base = new();
    private readonly TextBox _batch = new();
    private readonly TextBox _thick = new();
    private readonly CheckBox _coat = new() { Content = "涂布工序有异常", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _coatNote = new() { MinHeight = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };

    public WorkOrderParamsDialog()
    {
        Title = "开始称量 — 填写工艺参数";
        Width = 520;
        MinHeight = 420;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        _mix.Checked += (_, _) => _mixNote.IsEnabled = true;
        _mix.Unchecked += (_, _) => { _mixNote.IsEnabled = false; _mixNote.Text = ""; };
        _coat.Checked += (_, _) => _coatNote.IsEnabled = true;
        _coat.Unchecked += (_, _) => { _coatNote.IsEnabled = false; _coatNote.Text = ""; };

        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        form.Children.Add(UiTheme.SectionTitle("环境参数"));
        form.Children.Add(FieldRow("温度 * (℃)", _temp));
        form.Children.Add(FieldRow("湿度 * (%)", _hum));
        form.Children.Add(UiTheme.SectionTitle("生产信息"));
        form.Children.Add(FieldRow("生产编号 *", _prod));
        form.Children.Add(FieldRow("D50 (μm)", _d50));
        form.Children.Add(FieldRow("基膜 *", _base));
        form.Children.Add(FieldRow("批次", _batch));
        form.Children.Add(FieldRow("厚度", _thick));
        form.Children.Add(UiTheme.SectionTitle("异常说明"));
        form.Children.Add(_mix);
        form.Children.Add(FieldRow("混料异常说明", _mixNote));
        form.Children.Add(_coat);
        form.Children.Add(FieldRow("涂布异常说明", _coatNote));

        var cancel = UiTheme.GhostButton("取消");
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = UiTheme.PrimaryButton("开始称量");
        ok.Margin = new Thickness(0);
        ok.Click += (_, _) => Accept();

        Content = UiTheme.DialogBody(form, UiTheme.DialogFooterButtons(cancel, ok));
    }

    private static StackPanel FieldRow(string label, FrameworkElement input)
    {
        input.Margin = new Thickness(0, 0, 0, 10);
        return new StackPanel
        {
            Children = { UiTheme.FieldLabel(label), input }
        };
    }

    private void Accept()
    {
        if (!double.TryParse(_temp.Text, out var t) || !double.TryParse(_hum.Text, out var h))
        { MessageBox.Show(this, "请填写有效的温度和湿度"); return; }
        if (string.IsNullOrWhiteSpace(_prod.Text) || string.IsNullOrWhiteSpace(_base.Text))
        { MessageBox.Show(this, "生产编号与基膜为必填"); return; }
        if (_mix.IsChecked == true && string.IsNullOrWhiteSpace(_mixNote.Text))
        { MessageBox.Show(this, "请填写混料异常说明"); return; }
        if (_coat.IsChecked == true && string.IsNullOrWhiteSpace(_coatNote.Text))
        { MessageBox.Show(this, "请填写涂布异常说明"); return; }

        double? d50 = double.TryParse(_d50.Text, out var d) ? d : null;
        double? thick = double.TryParse(_thick.Text, out var th) ? th : null;

        Params = new WorkOrderParams
        {
            Temperature = t,
            Humidity = h,
            ProductionNo = _prod.Text.Trim(),
            D50 = d50,
            MixingException = _mix.IsChecked == true,
            MixingExceptionNote = _mix.IsChecked == true ? _mixNote.Text.Trim() : null,
            BaseFilm = _base.Text.Trim(),
            BatchNo = string.IsNullOrWhiteSpace(_batch.Text) ? null : _batch.Text.Trim(),
            Thickness = thick,
            CoatingException = _coat.IsChecked == true,
            CoatingExceptionNote = _coat.IsChecked == true ? _coatNote.Text.Trim() : null
        };
        DialogResult = true;
        Close();
    }
}
