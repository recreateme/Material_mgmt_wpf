using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;

namespace MaterialMgmt.App.Views.Dialogs;

public sealed record StepMaterialOption(int Id, string Code, string Display)
{
    public static StepMaterialOption From(Material m) => new(m.Id, m.Code, $"{m.Code}  {m.Name}");
}

public sealed record StepInput(int MaterialId, double Min, double Max, string? Notes);

/// <summary>
/// 添加 / 编辑配方步骤。上下限实时校验；添加时可“保存并继续添加”，表单清空后留在对话框里。
/// 写库由调用方的 <c>save</c> 完成：返回 null 表示成功，否则返回错误说明显示在表单里。
/// </summary>
public sealed class FormulaStepDialog : DialogWindow
{
    private readonly IReadOnlyList<StepMaterialOption> _options;
    private readonly Func<StepInput, string?> _save;
    private readonly Func<string>? _positionHint;
    private readonly ComboBox _material;
    private readonly TextBox _min;
    private readonly TextBox _max;
    private readonly TextBox _notes;
    private readonly TextBlock _rangeStatus = new()
    {
        FontSize = UiTheme.FontCaption,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, -4, 0, 10)
    };
    private readonly TextBlock? _position;
    private int _addedCount;

    /// <summary>本次对话框里成功添加的步骤数（含“保存并继续”）。</summary>
    public int AddedCount => _addedCount;

    /// <param name="positionHint">添加模式下的插入位置说明；每次“保存并继续”后刷新。编辑模式传 null。</param>
    public FormulaStepDialog(IReadOnlyList<StepMaterialOption> options, FormulaItem? existing,
        Func<string>? positionHint, Func<StepInput, string?> save)
        : base(existing is null ? "添加步骤" : "编辑步骤", 460)
    {
        _options = options;
        _save = save;
        _positionHint = existing is null ? positionHint : null;

        if (_positionHint is not null)
        {
            _position = UiTheme.Hint(_positionHint());
            _position.Margin = new Thickness(0, 0, 0, 12);
            Body.Children.Add(_position);
        }

        _material = AddField("原材料（可扫码）", new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(StepMaterialOption.Display)
        }, "下拉选择，或扫描原料条码自动选中");
        _material.SelectedItem = existing is null
            ? options.FirstOrDefault()
            : options.FirstOrDefault(o => o.Id == existing.MaterialId) ?? options.FirstOrDefault();

        _min = AddField("下限 (g) *", new TextBox { Text = existing?.WeightMin.ToString("0.###") ?? "" });
        _max = AddField("上限 (g) *", new TextBox { Text = existing?.WeightMax.ToString("0.###") ?? "" });
        Body.Children.Add(_rangeStatus);
        _notes = AddField("备注", new TextBox { Text = existing?.Notes ?? "", MaxLength = 200 });

        _min.TextChanged += (_, _) => UpdateRangeStatus();
        _max.TextChanged += (_, _) => UpdateRangeStatus();
        UpdateRangeStatus();

        if (existing is null)
        {
            var again = AddFooterButton(UiTheme.GhostButton("保存并继续添加"));
            again.Click += (_, _) => Submit(keepOpen: true);
        }
    }

    /// <summary>扫码选料；未找到返回 false。</summary>
    public bool TrySelectMaterial(string normalizedCode)
    {
        var match = _options.FirstOrDefault(o =>
            string.Equals(o.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            ShowError(_material, $"未找到原材料：{normalizedCode}");
            return false;
        }
        _material.SelectedItem = match;
        _min.Focus();
        return true;
    }

    protected override void OnConfirm() => Submit(keepOpen: false);

    private void Submit(bool keepOpen)
    {
        if (_material.SelectedItem is not StepMaterialOption pick)
        {
            ShowError(_material, "请选择原材料");
            return;
        }
        var error = StepRange.Validate(_min.Text, _max.Text, out var min, out var max);
        if (error is not null)
        {
            ShowError(error.StartsWith("上限") ? _max : _min, error);
            return;
        }

        var notes = string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text.Trim();
        var saveError = _save(new StepInput(pick.Id, min, max, notes));
        if (saveError is not null)
        {
            ShowError(_material, saveError);
            return;
        }

        _addedCount++;
        if (!keepOpen)
        {
            Accept();
            return;
        }

        // 连续添加：原料默认跳到下一项，上下限与备注清空
        var idx = _options.ToList().FindIndex(o => o.Id == pick.Id);
        if (idx >= 0 && idx + 1 < _options.Count) _material.SelectedItem = _options[idx + 1];
        _min.Text = "";
        _max.Text = "";
        _notes.Text = "";
        ClearErrors();
        if (_position is not null && _positionHint is not null)
            _position.Text = $"已添加 {_addedCount} 步。{_positionHint()}";
        _material.Focus();
    }

    private void UpdateRangeStatus()
    {
        if (string.IsNullOrWhiteSpace(_min.Text) && string.IsNullOrWhiteSpace(_max.Text))
        {
            _rangeStatus.Visibility = Visibility.Collapsed;
            return;
        }
        _rangeStatus.Visibility = Visibility.Visible;
        var error = StepRange.Validate(_min.Text, _max.Text, out var min, out var max);
        if (error is null)
        {
            _rangeStatus.Text = $"✓ 合格范围 {min:0.###} ~ {max:0.###} g";
            _rangeStatus.Foreground = UiTheme.Brush(UiTheme.Success);
        }
        else if (string.IsNullOrWhiteSpace(_max.Text))
        {
            _rangeStatus.Visibility = Visibility.Collapsed;
        }
        else
        {
            _rangeStatus.Text = error;
            _rangeStatus.Foreground = UiTheme.Brush(UiTheme.Danger);
        }
    }
}
