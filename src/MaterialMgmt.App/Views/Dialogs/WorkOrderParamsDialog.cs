using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;

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

    private readonly TextBox _temp = new() { Text = "25.0", Height = 28 };
    private readonly TextBox _hum = new() { Text = "50.0", Height = 28 };
    private readonly TextBox _prod = new() { Height = 28 };
    private readonly TextBox _d50 = new() { Height = 28 };
    private readonly CheckBox _mix = new() { Content = "混料工序有异常" };
    private readonly TextBox _mixNote = new() { Height = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _base = new() { Height = 28 };
    private readonly TextBox _batch = new() { Height = 28 };
    private readonly TextBox _thick = new() { Height = 28 };
    private readonly CheckBox _coat = new() { Content = "涂布工序有异常" };
    private readonly TextBox _coatNote = new() { Height = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };

    public WorkOrderParamsDialog()
    {
        Title = "开始称量 — 填写工艺参数";
        Width = 520; Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _mix.Checked += (_, _) => _mixNote.IsEnabled = true;
        _mix.Unchecked += (_, _) => { _mixNote.IsEnabled = false; _mixNote.Text = ""; };
        _coat.Checked += (_, _) => _coatNote.IsEnabled = true;
        _coat.Unchecked += (_, _) => { _coatNote.IsEnabled = false; _coatNote.Text = ""; };

        var form = new Grid { Margin = new Thickness(20) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new FrameworkElement[][]
        {
            new FrameworkElement[]{ L("温度 * (℃)"), _temp },
            new FrameworkElement[]{ L("湿度 * (%)"), _hum },
            new FrameworkElement[]{ L("生产编号 *"), _prod },
            new FrameworkElement[]{ L("D50 (μm)"), _d50 },
            new FrameworkElement[]{ L("混料异常"), _mix },
            new FrameworkElement[]{ L("混料异常说明"), _mixNote },
            new FrameworkElement[]{ L("基膜 *"), _base },
            new FrameworkElement[]{ L("批次"), _batch },
            new FrameworkElement[]{ L("厚度"), _thick },
            new FrameworkElement[]{ L("涂布异常"), _coat },
            new FrameworkElement[]{ L("涂布异常说明"), _coatNote },
        };
        for (var i = 0; i < rows.Length; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(rows[i][0], i); Grid.SetColumn(rows[i][0], 0);
            Grid.SetRow(rows[i][1], i); Grid.SetColumn(rows[i][1], 1);
            rows[i][1].Margin = new Thickness(0, 4, 0, 4);
            form.Children.Add(rows[i][0]); form.Children.Add(rows[i][1]);
        }

        var ok = UiTheme.PrimaryButton("开始称量");
        ok.Height = 34;
        ok.Margin = new Thickness(20, 0, 20, 16);
        ok.Click += (_, _) => Accept();

        var dock = new DockPanel();
        DockPanel.SetDock(ok, Dock.Bottom);
        dock.Children.Add(ok);
        dock.Children.Add(new ScrollViewer { Content = form });
        Content = dock;
    }

    private static TextBlock L(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };

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
