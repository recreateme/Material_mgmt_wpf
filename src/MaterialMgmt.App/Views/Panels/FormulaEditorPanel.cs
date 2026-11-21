using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class FormulaEditorPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly NavigationService _nav;
    private readonly TextBlock _breadcrumb = UiTheme.Breadcrumb("");
    private readonly TextBlock _title = new()
    {
        FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DataGrid _grid = new();
    private int _formulaId;
    private string _formulaCode = "";
    private List<FormulaItem> _items = new();
    private List<Material> _materials = new();
    private bool _orderDirty;
    private bool _stepDialogOpen;
    private ComboBox? _stepMaterialCombo;
    /// <summary>添加步骤时插入到该步骤之后；null 表示追加到末尾。</summary>
    private int? _addAfterItemId;

    public FormulaEditorPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth,
        IAuditLog audit, NavigationService nav)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _nav = nav;
        Build();
    }

    public void Load(int formulaId)
    {
        _formulaId = formulaId;
        _orderDirty = false;
        Reload();
    }

    public void OnShow()
    {
        if (_formulaId > 0) Reload();
    }

    public void OnBarcode(string code, bool simulated = false)
    {
        if (!_stepDialogOpen || _stepMaterialCombo is null) return;
        var normalized = DeviceHub.NormalizeCode(code);
        if (string.IsNullOrEmpty(normalized)) return;
        if (_stepMaterialCombo.ItemsSource is not IEnumerable<MaterialOption> options) return;
        var match = options.FirstOrDefault(o =>
            string.Equals(o.Code, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            MessageBox.Show($"未找到原材料：{normalized}", "扫码");
            return;
        }
        _stepMaterialCombo.SelectedItem = match;
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);

        header.Children.Add(_breadcrumb);
        var bar = new WrapPanel();
        var back = UiTheme.GhostButton("← 返回列表");
        back.Click += (_, _) => GoBack();
        bar.Children.Add(back);
        bar.Children.Add(_title);
        var add = UiTheme.PrimaryButton("添加步骤");
        add.Click += (_, _) => EditStep(null);
        bar.Children.Add(add);
        var up = UiTheme.GhostButton("上移");
        up.Click += (_, _) => Move(-1);
        bar.Children.Add(up);
        var down = UiTheme.GhostButton("下移");
        down.Click += (_, _) => Move(1);
        bar.Children.Add(down);
        var del = UiTheme.DangerButton("删除步骤");
        del.Click += (_, _) => DeleteStep();
        bar.Children.Add(del);
        var saveOrder = UiTheme.GhostButton("保存顺序");
        saveOrder.Click += (_, _) => SaveOrder();
        bar.Children.Add(saveOrder);
        var finish = UiTheme.PrimaryButton("完成");
        finish.Click += (_, _) => Complete();
        bar.Children.Add(finish);
        GridKit.AddSelectButtons(bar, _grid);

        header.Children.Add(bar);
        header.Children.Add(new TextBlock
        {
            Text = "请添加称量步骤并设置大于 0 的上下限。可扫码选料。" +
                   "选中某一步后再点「添加步骤」会插入到该步之后；未选中则追加到末尾。" +
                   "步骤较多时可滚动浏览（不分页）。调整顺序后点「保存顺序」或「完成」。",
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        GridKit.BindSingleActions(_grid, up, down);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("序号", "Item.Sequence", 60));
        _grid.Columns.Add(GridKit.Text("材料编码", "Item.Material.Code", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("材料名称", "Item.Material.Name", 160, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("下限(g)", "Item.WeightMin", 90, "0.###"));
        _grid.Columns.Add(GridKit.Text("上限(g)", "Item.WeightMax", 90, "0.###"));
        _grid.Columns.Add(GridKit.Text("备注", "Item.Notes", new DataGridLength(1, DataGridLengthUnitType.Star), align: TextAlignment.Left));
        _grid.MouseDoubleClick += (_, _) =>
        {
            if (CurrentStep() is { } it) EditStep(it);
        };

        root.Children.Add(header);
        root.Children.Add(_grid);
        Content = root;
    }

    private void Reload(int? selectItemId = null)
    {
        using var db = _dbFactory.CreateDbContext();
        var f = db.Formulas.Include(x => x.Items).ThenInclude(i => i.Material)
            .FirstOrDefault(x => x.Id == _formulaId);
        if (f is null) return;
        _formulaCode = f.Code;
        _breadcrumb.Text = $"配方管理  >  编辑 {f.Code}";
        _title.Text = f.Name;
        _materials = db.Materials.OrderBy(m => m.Code).ToList();
        _items = f.Items.OrderBy(i => i.Sequence).ToList();
        _grid.ItemsSource = GridKit.Wrap(_items);
        _orderDirty = false;

        if (selectItemId is int sid)
        {
            var row = _grid.Items.OfType<SelectableRow<FormulaItem>>()
                .FirstOrDefault(r => r.Item.Id == sid);
            if (row != null)
            {
                _grid.SelectedItem = row;
                _grid.ScrollIntoView(row);
            }
        }
    }

    private FormulaItem? CurrentStep()
        => GridKit.Current<FormulaItem>(_grid)?.Item;

    private void GoBack()
    {
        if (_orderDirty)
        {
            var r = MessageBox.Show("步骤顺序尚未保存。是否保存后返回？", "确认",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !SaveOrder(silent: true)) return;
        }
        else if (_items.Count == 0)
        {
            if (!ConfirmDialog.Ask("该配方尚无称量步骤，确定返回列表？", "确认", "确定"))
                return;
        }
        _nav.Navigate(AppPage.Formulas);
    }

    private void EditStep(FormulaItem? existing)
    {
        if (_materials.Count == 0) { MessageBox.Show("请先创建原材料"); return; }

        // 新增：记住当前光标行，保存时插到其后；无选中则追加末尾
        _addAfterItemId = existing is null ? CurrentStep()?.Id : null;

        var options = _materials.Select(m => new MaterialOption(m)).ToList();
        var mat = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            ItemsSource = options,
            DisplayMemberPath = nameof(MaterialOption.Display)
        };
        mat.SelectedItem = existing is null
            ? options[0]
            : options.FirstOrDefault(o => o.Id == existing.MaterialId) ?? options[0];

        var min = new TextBox
        {
            Text = existing is null ? "" : existing.WeightMin.ToString("F3"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var max = new TextBox
        {
            Text = existing is null ? "" : existing.WeightMax.ToString("F3"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var notes = new TextBox { Text = existing?.Notes ?? "", Margin = new Thickness(0, 0, 0, 10) };

        var dlg = new Window
        {
            Title = existing is null ? "添加步骤" : "编辑步骤",
            Width = 440,
            MinHeight = 420,
            MaxHeight = 640,
            SizeToContent = SizeToContent.Height,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        form.Children.Add(UiTheme.FieldLabel("原材料（可扫码）"));
        form.Children.Add(mat);
        form.Children.Add(UiTheme.Hint("下拉选择，或扫描原料条码自动选中"));
        if (existing is null)
        {
            form.Children.Add(UiTheme.Hint(_addAfterItemId is null
                ? "未选中步骤：将追加到列表末尾"
                : "已选中步骤：将插入到该步骤之后"));
        }
        form.Children.Add(UiTheme.FieldLabel("下限 (g) *"));
        form.Children.Add(min);
        form.Children.Add(UiTheme.FieldLabel("上限 (g) *"));
        form.Children.Add(max);
        form.Children.Add(UiTheme.FieldLabel("备注"));
        form.Children.Add(notes);

        var cancel = UiTheme.GhostButton("取消");
        cancel.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
        var ok = UiTheme.PrimaryButton("保存");
        ok.Margin = new Thickness(0);
        ok.Click += (_, _) => SaveStep(dlg, existing, mat, min, max, notes);
        dlg.Content = UiTheme.DialogBody(form, UiTheme.DialogFooterButtons(cancel, ok));

        _stepMaterialCombo = mat;
        _stepDialogOpen = true;
        try { dlg.ShowDialog(); }
        finally
        {
            _stepDialogOpen = false;
            _stepMaterialCombo = null;
            _addAfterItemId = null;
        }
    }

    private void SaveStep(Window dlg, FormulaItem? existing, ComboBox mat, TextBox min, TextBox max, TextBox notes)
    {
        if (mat.SelectedItem is not MaterialOption pick)
        {
            MessageBox.Show(dlg, "请选择原材料", "提示");
            return;
        }
        if (!double.TryParse(min.Text, out var wmin) || !double.TryParse(max.Text, out var wmax)
            || wmin <= 0 || wmax <= 0 || wmin > wmax)
        {
            MessageBox.Show(dlg, "请输入大于 0 的上下限（下限 ≤ 上限）", "提示");
            return;
        }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            int? focusId = existing?.Id;
            if (existing is null)
            {
                var ordered = db.FormulaItems
                    .Where(i => i.FormulaId == _formulaId)
                    .OrderBy(i => i.Sequence)
                    .ToList();
                var insertIndex = ordered.Count;
                if (_addAfterItemId is int afterId)
                {
                    var idx = ordered.FindIndex(x => x.Id == afterId);
                    if (idx >= 0) insertIndex = idx + 1;
                }

                var entity = new FormulaItem
                {
                    FormulaId = _formulaId,
                    MaterialId = pick.Id,
                    Sequence = FormulaSequence.TempOffset + ordered.Count + 1,
                    WeightMin = wmin,
                    WeightMax = wmax,
                    Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim()
                };
                db.FormulaItems.Add(entity);
                db.SaveChanges();
                focusId = entity.Id;

                var ids = ordered.Select(x => x.Id).ToList();
                ids.Insert(insertIndex, entity.Id);
                ApplySequencePhases(db, ids);
            }
            else
            {
                var e = db.FormulaItems.First(i => i.Id == existing.Id);
                e.MaterialId = pick.Id;
                e.WeightMin = wmin;
                e.WeightMax = wmax;
                e.Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
                db.SaveChanges();
            }

            _audit.Log("formula", $"编辑配方步骤 formulaId={_formulaId}", _auth.CurrentUser?.Username ?? "");
            dlg.DialogResult = true;
            dlg.Close();
            Reload(focusId);
            UiFeedback.Success(existing is null ? "步骤已添加。" : "步骤已保存。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(dlg, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Move(int dir)
    {
        if (CurrentStep() is not { } it)
        {
            MessageBox.Show("请先选择一个步骤", "提示");
            return;
        }
        var idx = _items.FindIndex(x => x.Id == it.Id);
        var target = idx + dir;
        if (target < 0 || target >= _items.Count) return;
        (_items[idx], _items[target]) = (_items[target], _items[idx]);
        for (var i = 0; i < _items.Count; i++) _items[i].Sequence = i + 1;
        _grid.ItemsSource = GridKit.Wrap(_items);
        var row = _grid.Items.OfType<SelectableRow<FormulaItem>>()
            .FirstOrDefault(r => r.Item.Id == _items[target].Id);
        if (row != null)
        {
            _grid.SelectedItem = row;
            _grid.ScrollIntoView(row);
        }
        _orderDirty = true;
    }

    private void DeleteStep()
    {
        var selected = GridKit.CheckedItems<FormulaItem>(_grid);
        if (selected.Count == 0 && CurrentStep() is { } one)
            selected = new List<FormulaItem> { one };
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选或选中要删除的步骤", "提示");
            return;
        }

        var tip = selected.Count == 1
            ? "确认删除该步骤？"
            : $"确认删除选中的 {selected.Count} 个步骤？";
        if (!ConfirmDialog.Ask(tip, "确认删除", "删除", danger: true))
            return;

        using var db = _dbFactory.CreateDbContext();
        var ids = selected.Select(x => x.Id).ToHashSet();
        var entities = db.FormulaItems.Where(x => ids.Contains(x.Id)).ToList();
        db.FormulaItems.RemoveRange(entities);
        db.SaveChanges();

        var remain = db.FormulaItems
            .Where(i => i.FormulaId == _formulaId)
            .OrderBy(i => i.Sequence)
            .Select(i => i.Id)
            .ToList();
        if (remain.Count > 0)
            ApplySequencePhases(db, remain);

        Reload();
        UiFeedback.Success(selected.Count == 1 ? "步骤已删除。" : $"已删除 {selected.Count} 个步骤。");
    }

    private bool SaveOrder(bool silent = false)
    {
        if (_items.Count == 0) return true;
        try
        {
            var ids = _items.Select(x => x.Id).ToList();
            using var db = _dbFactory.CreateDbContext();
            ApplySequencePhases(db, ids);
            Reload();
            if (!silent)
                UiFeedback.Success("步骤顺序已保存。");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存顺序失败：{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void ApplySequencePhases(MaterialMgmtDbContext db, IReadOnlyList<int> orderedIds)
    {
        var phases = FormulaSequence.BuildTwoPhase(orderedIds);
        foreach (var (id, seq) in phases.Take(orderedIds.Count))
            db.FormulaItems.First(x => x.Id == id).Sequence = seq;
        db.SaveChanges();
        foreach (var (id, seq) in phases.Skip(orderedIds.Count))
            db.FormulaItems.First(x => x.Id == id).Sequence = seq;
        db.SaveChanges();
    }

    private void Complete()
    {
        if (_items.Count == 0)
        {
            UiFeedback.Warn("请至少添加一个称量步骤后再完成。");
            return;
        }

        if (!SaveOrder(silent: true)) return;
        _audit.Log("formula", $"完成配方编辑 {_formulaCode}", _auth.CurrentUser?.Username ?? "");
        UiFeedback.Success($"配方 {_formulaCode} 已保存，共 {_items.Count} 个步骤。", "配方编辑完成");
        _nav.Navigate(AppPage.Formulas);
    }

    private sealed class MaterialOption
    {
        public MaterialOption(Material m)
        {
            Id = m.Id;
            Code = m.Code;
            Display = $"{m.Code}  {m.Name}";
        }
        public int Id { get; }
        public string Code { get; }
        public string Display { get; }
    }
}
