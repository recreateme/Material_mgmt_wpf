using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>
/// 开始称量前填写工艺参数。
/// 温度/湿度不再预填 25.0/50.0：有“上次的值”时带入并高亮，必须核对（修改或点选该框）后才能开始；
/// 没有上次的值则留空。基膜、批次沿用上次的值。D50/厚度填了但不是有效数字时在字段旁报错，不再静默丢弃。
/// </summary>
public sealed class WorkOrderParamsDialog : DialogWindow
{
    private const string Section = "weigh_defaults";

    private readonly IConfigService _config;

    public WorkOrderParams? Params { get; private set; }

    private readonly TextBox _temp = new();
    private readonly TextBox _hum = new();
    private readonly TextBox _prod = new();
    private readonly TextBox _d50 = new();
    private readonly CheckBox _mix = new() { Content = "混料工序有异常", Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _mixNote = new() { MinHeight = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _base = new();
    private readonly TextBox _batch = new();
    private readonly TextBox _thick = new();
    private readonly CheckBox _coat = new() { Content = "涂布工序有异常", Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _coatNote = new() { MinHeight = 48, AcceptsReturn = true, IsEnabled = false, TextWrapping = TextWrapping.Wrap };

    // 预填的环境参数是否已被操作员核对
    private bool _tempPrefilled, _tempConfirmed;
    private bool _humPrefilled, _humConfirmed;

    public WorkOrderParamsDialog(IConfigService config)
        : base("开始称量 — 填写工艺参数", 520, "开始称量", "取消")
    {
        _config = config;

        var lastTemp = _config.GetValue(Section, "temperature", "");
        var lastHum = _config.GetValue(Section, "humidity", "");
        _base.Text = _config.GetValue(Section, "base_film", "");
        _batch.Text = _config.GetValue(Section, "batch_no", "");

        Body.Children.Add(UiTheme.SectionTitle("环境参数"));
        AddField("温度 * (℃)", _temp,
            lastTemp.Length > 0 ? "已带入上次的值，请对照现场温度核对" : "请读取现场温度后填写");
        AddField("湿度 * (%)", _hum,
            lastHum.Length > 0 ? "已带入上次的值，请对照现场湿度核对" : "请读取现场湿度后填写");
        PrefillEnvironment(_temp, lastTemp, () => _tempPrefilled = true, () => _tempConfirmed = true);
        PrefillEnvironment(_hum, lastHum, () => _humPrefilled = true, () => _humConfirmed = true);

        Body.Children.Add(UiTheme.SectionTitle("生产信息"));
        AddField("生产编号 *", _prod);
        AddField("D50 (μm)", _d50);
        AddField("基膜 *", _base);
        AddField("批次", _batch);
        AddField("厚度", _thick);

        Body.Children.Add(UiTheme.SectionTitle("异常说明"));
        Body.Children.Add(_mix);
        AddField("混料异常说明", _mixNote);
        Body.Children.Add(_coat);
        AddField("涂布异常说明", _coatNote);

        _mix.Checked += (_, _) => _mixNote.IsEnabled = true;
        _mix.Unchecked += (_, _) => { _mixNote.IsEnabled = false; _mixNote.Text = ""; };
        _coat.Checked += (_, _) => _coatNote.IsEnabled = true;
        _coat.Unchecked += (_, _) => { _coatNote.IsEnabled = false; _coatNote.Text = ""; };
    }

    /// <summary>有上次的值：带入、全选并用浅色底高亮，直到操作员修改或点选/按键。</summary>
    private static void PrefillEnvironment(TextBox box, string last, Action markPrefilled, Action markConfirmed)
    {
        if (last.Length == 0) return;

        box.Text = last;
        markPrefilled();
        box.Background = UiTheme.Brush(UiTheme.WithAlpha(UiTheme.Warning, 40));
        box.GotKeyboardFocus += (_, _) => box.SelectAll();

        var done = false;
        void Confirmed()
        {
            if (done) return;
            done = true;
            markConfirmed();
            box.ClearValue(Control.BackgroundProperty);
        }

        box.TextChanged += (_, _) => Confirmed();
        box.PreviewKeyDown += (_, _) => Confirmed();
        box.PreviewMouseDown += (_, _) => Confirmed();
    }

    protected override void OnConfirm()
    {
        ClearErrors();

        if (!TryNumber(_temp.Text, out var t))
        { ShowError(_temp, "请填写有效的温度（数字）"); return; }
        if (t < -20 || t > 80)
        { ShowError(_temp, "温度应在 -20 ~ 80 ℃ 之间，请核对"); return; }
        if (_tempPrefilled && !_tempConfirmed)
        { ShowError(_temp, "请核对温度：与上次相同请点一下该框或按任意键确认，不同请直接修改"); return; }

        if (!TryNumber(_hum.Text, out var h))
        { ShowError(_hum, "请填写有效的湿度（数字）"); return; }
        if (h < 0 || h > 100)
        { ShowError(_hum, "湿度应在 0 ~ 100 % 之间，请核对"); return; }
        if (_humPrefilled && !_humConfirmed)
        { ShowError(_hum, "请核对湿度：与上次相同请点一下该框或按任意键确认，不同请直接修改"); return; }

        if (string.IsNullOrWhiteSpace(_prod.Text)) { ShowError(_prod, "生产编号为必填"); return; }

        double? d50 = null;
        if (!string.IsNullOrWhiteSpace(_d50.Text))
        {
            if (!TryNumber(_d50.Text, out var d) || d <= 0)
            { ShowError(_d50, "D50 需为大于 0 的数字，不填请留空"); return; }
            d50 = d;
        }

        if (string.IsNullOrWhiteSpace(_base.Text)) { ShowError(_base, "基膜为必填"); return; }

        double? thick = null;
        if (!string.IsNullOrWhiteSpace(_thick.Text))
        {
            if (!TryNumber(_thick.Text, out var th) || th <= 0)
            { ShowError(_thick, "厚度需为大于 0 的数字，不填请留空"); return; }
            thick = th;
        }

        if (_mix.IsChecked == true && string.IsNullOrWhiteSpace(_mixNote.Text))
        { ShowError(_mixNote, "请填写混料异常说明"); return; }
        if (_coat.IsChecked == true && string.IsNullOrWhiteSpace(_coatNote.Text))
        { ShowError(_coatNote, "请填写涂布异常说明"); return; }

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

        RememberLastValues(t, h);
        Accept();
    }

    /// <summary>记住这次填写的值，下次作为“上次的值”带入。保存失败不影响开始称量。</summary>
    private void RememberLastValues(double t, double h)
    {
        try
        {
            _config.SetValue(Section, "temperature", t.ToString("0.#", CultureInfo.InvariantCulture));
            _config.SetValue(Section, "humidity", h.ToString("0.#", CultureInfo.InvariantCulture));
            _config.SetValue(Section, "base_film", _base.Text.Trim());
            _config.SetValue(Section, "batch_no", _batch.Text.Trim());
        }
        catch
        {
            // 仅便利功能
        }
    }

    private static bool TryNumber(string text, out double value)
        => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && !double.IsNaN(value) && !double.IsInfinity(value);
}
