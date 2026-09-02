using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class FormulaEditorPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly NavigationService _nav;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.Bold };
    private readonly DataGrid _grid = new();
    private int _formulaId;
    private string _formulaCode = "";
    private List<FormulaItem> _items = new();
    private List<Material> _materials = new();

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
        Reload();
    }

    public void OnShow()
    {
        if (_formulaId > 0) Reload();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var back = UiTheme.GhostButton("← 返回列表");
        back.Click += (_, _) => _nav.Navigate(AppPage.Formulas);
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
            Text = "请添加称量步骤并设置上下限；调整顺序后点「保存顺序」。全部完成后点「完成」返回配方列表。",
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
        _title.Text = $"编辑配方：{f.Code}  {f.Name}";
        _materials = db.Materials.OrderBy(m => m.Code).ToList();
        _items = f.Items.OrderBy(i => i.Sequence).ToList();
        _grid.ItemsSource = null;
        _grid.ItemsSource = _items;
    }

    private void EditStep(FormulaItem? existing)
    {
        if (_materials.Count == 0) { MessageBox.Show("请先创建原材料"); return; }
        var mat = new ComboBox
        {
            Height = 30, Margin = new Thickness(0, 4, 0, 10),
            ItemsSource = _materials, DisplayMemberPath = "Name", SelectedValuePath = "Id",
            SelectedValue = existing?.MaterialId ?? _materials[0].Id
        };
        var min = new TextBox { Height = 30, Text = (existing?.WeightMin ?? 0).ToString("F3"), Margin = new Thickness(0, 4, 0, 10) };
        var max = new TextBox { Height = 30, Text = (existing?.WeightMax ?? 0).ToString("F3"), Margin = new Thickness(0, 4, 0, 10) };
        var notes = new TextBox { Height = 30, Text = existing?.Notes ?? "", Margin = new Thickness(0, 4, 0, 10) };

        var dlg = new Window
        {
            Title = existing is null ? "添加步骤" : "编辑步骤", Width = 400, Height = 340,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "材料" }); stack.Children.Add(mat);
        stack.Children.Add(new TextBlock { Text = "下限 (g)" }); stack.Children.Add(min);
        stack.Children.Add(new TextBlock { Text = "上限 (g)" }); stack.Children.Add(max);
        stack.Children.Add(new TextBlock { Text = "备注" }); stack.Children.Add(notes);
        var ok = UiTheme.PrimaryButton("保存");
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(min.Text, out var wmin) || !double.TryParse(max.Text, out var wmax) || wmin > wmax)
            {
                MessageBox.Show(dlg, "请输入有效的上下限（下限 ≤ 上限）");
                return;
            }
            using var db = _dbFactory.CreateDbContext();
            if (existing is null)
            {
                var seq = db.FormulaItems.Where(i => i.FormulaId == _formulaId).Select(i => (int?)i.Sequence).Max() ?? 0;
                db.FormulaItems.Add(new FormulaItem
                {
                    FormulaId = _formulaId,
                    MaterialId = (int)mat.SelectedValue!,
                    Sequence = seq + 1,
                    WeightMin = wmin,
                    WeightMax = wmax,
                    Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim()
                });
            }
            else
            {
                var e = db.FormulaItems.First(i => i.Id == existing.Id);
                e.MaterialId = (int)mat.SelectedValue!;
                e.WeightMin = wmin;
                e.WeightMax = wmax;
                e.Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            }
            db.SaveChanges();
            _audit.Log("formula", $"编辑配方步骤 formulaId={_formulaId}", _auth.CurrentUser?.Username ?? "");
            dlg.DialogResult = true;
            dlg.Close();
            Reload();
        };
        stack.Children.Add(ok);
        dlg.Content = stack;
        dlg.ShowDialog();
    }

    private void Move(int dir)
    {
        if (_grid.SelectedItem is not FormulaItem it) return;
        var idx = _items.FindIndex(x => x.Id == it.Id);
        var target = idx + dir;
        if (target < 0 || target >= _items.Count) return;
        (_items[idx], _items[target]) = (_items[target], _items[idx]);
        for (var i = 0; i < _items.Count; i++) _items[i].Sequence = i + 1;
        _grid.ItemsSource = null;
        _grid.ItemsSource = _items;
        _grid.SelectedIndex = target;
    }

    private void DeleteStep()
    {
        if (_grid.SelectedItem is not FormulaItem it) return;
        if (MessageBox.Show("确认删除该步骤？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        using var db = _dbFactory.CreateDbContext();
        var e = db.FormulaItems.First(x => x.Id == it.Id);
        db.FormulaItems.Remove(e);
        db.SaveChanges();
        Reload();
        SaveOrder(silent: true);
    }

    private void SaveOrder(bool silent = false)
    {
        if (_items.Count == 0) return;
        using var db = _dbFactory.CreateDbContext();
        for (var i = 0; i < _items.Count; i++)
        {
            var e = db.FormulaItems.First(x => x.Id == _items[i].Id);
            e.Sequence = i + 1;
        }
        db.SaveChanges();
        Reload();
        if (!silent)
            UiFeedback.Success("步骤顺序已保存。");
    }

    private void Complete()
    {
        if (_items.Count == 0)
        {
            UiFeedback.Warn("请至少添加一个称量步骤后再完成。");
            return;
        }

        SaveOrder(silent: true);
        _audit.Log("formula", $"完成配方编辑 {_formulaCode}", _auth.CurrentUser?.Username ?? "");
        UiFeedback.Success($"配方 {_formulaCode} 已保存，共 {_items.Count} 个步骤。", "配方编辑完成");
        _nav.Navigate(AppPage.Formulas);
    }
}
