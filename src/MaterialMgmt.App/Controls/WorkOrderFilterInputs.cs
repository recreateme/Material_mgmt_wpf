using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Orders;

namespace MaterialMgmt.App.Controls;

/// <summary>工单列表的筛选输入：文本搜索 + 状态 + 开始日期范围 + 配方，挂在 <see cref="PageHeader"/> 上。“我的工单”和“工单历史”共用。</summary>
public sealed class WorkOrderFilterInputs
{
    private sealed record Option(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox _search;
    private readonly ComboBox _status = new() { Width = 110 };
    private readonly DatePicker _from = new() { Width = 130 };
    private readonly DatePicker _to = new() { Width = 130 };
    private readonly ComboBox _formula = new() { Width = 200 };
    private bool _suppress;

    public event Action? Changed;

    public WorkOrderFilterInputs(PageHeader header, TextBox search, string placeholder)
    {
        _search = search;
        header.AddSearch(search, placeholder);

        _status.ItemsSource = new[]
        {
            new Option("全部", null),
            new Option(OrderRules.ToDisplay(OrderRules.InProgress), OrderRules.InProgress),
            new Option(OrderRules.ToDisplay(OrderRules.Completed), OrderRules.Completed),
            new Option(OrderRules.ToDisplay(OrderRules.Cancelled), OrderRules.Cancelled)
        };
        _status.SelectedIndex = 0;
        SetFormulas(Array.Empty<(int, string)>());

        header.AddFilter("状态", _status);
        header.AddFilter("开始日期", _from);
        header.AddFilter("至", _to);
        header.AddFilter("配方", _formula);
        var clear = header.AddFilterButton(UiTheme.GhostButton("清除筛选"));
        clear.Click += (_, _) => Clear();

        search.TextChanged += (_, _) => Raise();
        _status.SelectionChanged += (_, _) => Raise();
        _from.SelectedDateChanged += (_, _) => Raise();
        _to.SelectedDateChanged += (_, _) => Raise();
        _formula.SelectionChanged += (_, _) => Raise();
    }

    public WorkOrderFilter Current => new(
        Status: (_status.SelectedItem as Option)?.Value as string,
        From: _from.SelectedDate,
        To: _to.SelectedDate,
        FormulaId: (_formula.SelectedItem as Option)?.Value as int?,
        Text: _search.Text);

    /// <summary>刷新配方下拉（来自当前列表里出现过的配方），尽量保留原选择。</summary>
    public void SetFormulas(IEnumerable<(int Id, string Name)> formulas)
    {
        var keep = (_formula.SelectedItem as Option)?.Value as int?;
        var options = new List<Option> { new("全部配方", null) };
        options.AddRange(formulas.DistinctBy(f => f.Id).OrderBy(f => f.Name).Select(f => new Option(f.Name, f.Id)));
        _suppress = true;
        try
        {
            _formula.ItemsSource = options;
            _formula.SelectedItem = options.FirstOrDefault(o => o.Value as int? == keep) ?? options[0];
        }
        finally { _suppress = false; }
    }

    public void Clear()
    {
        _suppress = true;
        try
        {
            _search.Text = "";
            _status.SelectedIndex = 0;
            _from.SelectedDate = null;
            _to.SelectedDate = null;
            _formula.SelectedIndex = 0;
        }
        finally { _suppress = false; }
        Changed?.Invoke();
    }

    private void Raise()
    {
        if (!_suppress) Changed?.Invoke();
    }
}
