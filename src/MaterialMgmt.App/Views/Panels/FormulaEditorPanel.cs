using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class FormulaEditorPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly NavigationService _nav;
    private readonly TextBlock _breadcrumb = UiTheme.Breadcrumb("");
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _grid = new();
    private int _formulaId;
    private string _formulaCode = "";
    private List<FormulaItem> _items = new();
    private List<Material> _materials = new();
    private bool _orderDirty;
    private bool _stepDialogOpen;
    private ComboBox? _stepMaterialCombo;

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
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
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

        header.Children.Add(bar);
        header.Children.Add(new TextBlock
        {
            Text = "请添加称量步骤并设置大于 0 的上下限。可扫码选择原材料。调整顺序后点「保存顺序」或「完成」。",
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new System.Windows.Data.Binding("Sequence"), Width = 60 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "材料编码", Binding = new System.Windows.Data.Binding("Material.Code"), Width = 120 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "材料名称", Binding = new System.Windows.Data.Binding("Material.Name"), Width = 160 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "下限(g)", Binding = new System.Windows.Data.Binding("WeightMin"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "上限(g)", Binding = new System.Windows.Data.Binding("WeightMax"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.MouseDoubleClick += (_, _) => { if (_grid.SelectedItem is FormulaItem it) EditStep(it); };

        root.Children.Add(header);
        root.Children.Add(_grid);
        Content = root;
    }

    private void Reload()
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
        _grid.ItemsSource = null;
        _grid.ItemsSource = _items;
        _orderDirty = false;
    }

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
            if (MessageBox.Show("该配方尚无称量步骤，确定返回列表？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }
        _nav.Navigate(AppPage.Formulas);
    }

    private void EditStep(FormulaItem? existing)
    {
        if (_materials.Count == 0) { MessageBox.Show("请先创建原材料"); return; }

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
        form.Children.Add(UiTheme.FieldLabel("下限 (g) *"));
        form.Children.Add(min);
        form.Children.Add(UiTheme.FieldLabel("上限 (g) *"));
        form.Children.Add(max);
        form.Children.Add(UiTheme.FieldLabel("备注"));
        form.Children.Add(notes);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 8, 20, 16)
        };
        var cancel = UiTheme.GhostButton("取消");
        cancel.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
        var ok = UiTheme.PrimaryButton("保存");
        ok.Margin = new Thickness(0);
        ok.Click += (_, _) => SaveStep(dlg, existing, mat, min, max, notes);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dlg.Content = dock;

        _stepMaterialCombo = mat;
        _stepDialogOpen = true;
        try { dlg.ShowDialog(); }
        finally
        {
            _stepDialogOpen = false;
            _stepMaterialCombo = null;
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
            if (existing is null)
            {
                var seq = db.FormulaItems.Where(i => i.FormulaId == _formulaId).Select(i => (int?)i.Sequence).Max() ?? 0;
                db.FormulaItems.Add(new FormulaItem
                {
                    FormulaId = _formulaId,
                    MaterialId = pick.Id,
                    Sequence = seq + 1,
                    WeightMin = wmin,
                    WeightMax = wmax,
                    Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim()
                });
            }
            else
            {
                var e = db.FormulaItems.First(i => i.Id == existing.Id);
                e.MaterialId = pick.Id;
                e.WeightMin = wmin;
                e.WeightMax = wmax;
                e.Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            }
            db.SaveChanges();
            _audit.Log("formula", $"编辑配方步骤 formulaId={_formulaId}", _auth.CurrentUser?.Username ?? "");
            dlg.DialogResult = true;
            dlg.Close();
            Reload();
            UiFeedback.Success(existing is null ? "步骤已添加。" : "步骤已保存。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(dlg, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Move(int dir)
    {
        if (_grid.SelectedItem is not FormulaItem it)
        {
            MessageBox.Show("请先选择一个步骤", "提示");
            return;
        }
        var idx = _items.FindIndex(x => x.Id == it.Id);
        var target = idx + dir;
        if (target < 0 || target >= _items.Count) return;
        (_items[idx], _items[target]) = (_items[target], _items[idx]);
        for (var i = 0; i < _items.Count; i++) _items[i].Sequence = i + 1;
        _grid.ItemsSource = null;
        _grid.ItemsSource = _items;
        _grid.SelectedIndex = target;
        _orderDirty = true;
    }

    private void DeleteStep()
    {
        if (_grid.SelectedItem is not FormulaItem it)
        {
            MessageBox.Show("请先选择一个步骤", "提示");
            return;
        }
        if (MessageBox.Show("确认删除该步骤？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        using var db = _dbFactory.CreateDbContext();
        var e = db.FormulaItems.First(x => x.Id == it.Id);
        db.FormulaItems.Remove(e);
        db.SaveChanges();
        Reload();
        SaveOrder(silent: true);
    }

    private bool SaveOrder(bool silent = false)
    {
        if (_items.Count == 0) return true;
        try
        {
            var ids = _items.Select(x => x.Id).ToList();
            var phases = FormulaSequence.BuildTwoPhase(ids);
            using var db = _dbFactory.CreateDbContext();
            foreach (var (id, seq) in phases.Take(ids.Count))
                db.FormulaItems.First(x => x.Id == id).Sequence = seq;
            db.SaveChanges();
            foreach (var (id, seq) in phases.Skip(ids.Count))
                db.FormulaItems.First(x => x.Id == id).Sequence = seq;
            db.SaveChanges();
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
