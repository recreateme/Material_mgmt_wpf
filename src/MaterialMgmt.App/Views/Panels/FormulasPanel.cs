using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class FormulasPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly FormulaService _formulas;
    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new() { Width = 200, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
    private readonly PaginationBar _pager = new(10);
    private List<Formula> _all = new();
    private List<Formula> _filtered = new();
    private bool _createDialogOpen;

    public FormulasPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth,
        IAuditLog audit, FormulaService formulas, NavigationService nav, DeviceHub devices)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _formulas = formulas;
        _nav = nav;
        _devices = devices;
        Build();
    }

    public void OnShow() => Refresh();

    public void OnBarcode(string code, bool simulated = false)
    {
        if (_createDialogOpen) return;
        BarcodeFillHelper.FillFilter(_filter, code);
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        bar.Children.Add(new TextBlock { Text = "配方管理", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 16, 8), VerticalAlignment = VerticalAlignment.Center });
        _filter.Margin = new Thickness(0, 0, 8, 8);
        bar.Children.Add(_filter);
        _filter.TextChanged += (_, _) => { _pager.Reset(); Render(); };
        var add = UiTheme.PrimaryButton("新建配方");
        add.Click += (_, _) => Create();
        bar.Children.Add(add);
        var edit = UiTheme.PrimaryButton("编辑步骤");
        edit.Click += (_, _) =>
        {
            if (CurrentFormula() is { } row)
                _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(row.Id));
            else
                MessageBox.Show("请先选择一条配方", "提示");
        };
        bar.Children.Add(edit);
        var copy = UiTheme.GhostButton("复制配方");
        copy.Click += (_, _) => CopySelected();
        bar.Children.Add(copy);
        var toggle = UiTheme.GhostButton("启用/停用");
        toggle.Click += (_, _) => ToggleActive();
        bar.Children.Add(toggle);
        var del = UiTheme.DangerButton("删除选中");
        del.Click += (_, _) => DeleteSelected();
        bar.Children.Add(del);
        GridKit.AddSelectButtons(bar, _grid);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        GridKit.BindSingleActions(_grid, edit, copy);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("编码", "Item.Code", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("名称", "Item.Name", 200, align: TextAlignment.Left));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "状态",
            Binding = new System.Windows.Data.Binding("Item.IsActive") { Converter = new BoolActiveConverter() },
            Width = 90,
            ElementStyle = GridKit.CenteredTextStyle
        });
        _grid.Columns.Add(GridKit.Text("创建人", "Item.CreatedBy", 100));
        _grid.Columns.Add(GridKit.Text("创建时间", "Item.CreatedAt", 160, "yyyy-MM-dd HH:mm"));
        _grid.MouseDoubleClick += (_, _) =>
        {
            if (GridKit.Current<Formula>(_grid) is { } row)
                _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(row.Item.Id));
        };

        _pager.PageChanged += Render;
        DockPanel.SetDock(_pager, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_pager);
        root.Children.Add(_grid);
        Content = root;
    }

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        _all = db.Formulas.OrderByDescending(f => f.Id).ToList();
        Render();
    }

    private void Render()
    {
        var q = _filter.Text.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(f =>
                f.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.ItemsSource = GridKit.Wrap(_pager.Apply(_filtered));
    }

    private void Create()
    {
        var nameBox = new TextBox { Height = 30, Margin = new Thickness(0, 4, 0, 10) };
        var codeBox = new TextBox { Height = 30, Margin = new Thickness(0, 4, 0, 10), IsEnabled = false };
        var manual = new CheckBox { Content = "手工指定编码", Margin = new Thickness(0, 0, 0, 6) };
        using (var db = _dbFactory.CreateDbContext())
        {
            var last = db.Formulas.OrderByDescending(f => f.Id).Select(f => (int?)f.Id).FirstOrDefault();
            codeBox.Text = CodeService.PreviewNext(CodeKind.Formula, last);
        }
        manual.Checked += (_, _) => codeBox.IsEnabled = true;
        manual.Unchecked += (_, _) => codeBox.IsEnabled = false;

        var dlg = new Window
        {
            Title = "新建配方", Width = 400, SizeToContent = SizeToContent.Height, MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this)
        };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "名称 *" });
        stack.Children.Add(nameBox);
        stack.Children.Add(manual);
        stack.Children.Add(new TextBlock { Text = "编码" });
        stack.Children.Add(codeBox);
        var ok = UiTheme.PrimaryButton("创建");
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) { MessageBox.Show(dlg, "请填写名称"); return; }
            try
            {
                using var db = _dbFactory.CreateDbContext();
                var code = ResolveCreateCode(db, manual.IsChecked == true, codeBox.Text);
                var f = new Formula
                {
                    Name = nameBox.Text.Trim(),
                    CreatedBy = _auth.CurrentUser?.DisplayName,
                    CreatedAt = DateTime.Now,
                    IsActive = true,
                    Code = code
                };
                db.Formulas.Add(f);
                db.SaveChanges();
                _audit.Log("formula", $"新建配方 {f.Code}", _auth.CurrentUser?.Username ?? "");
                dlg.DialogResult = true;
                dlg.Close();
                Refresh();
                _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(f.Id));
            }
            catch (Exception ex) { MessageBox.Show(dlg, ex.Message, "失败"); }
        };
        stack.Children.Add(ok);
        dlg.Content = stack;
        BarcodeFillHelper.AttachDialog(dlg, _devices, manual, codeBox);
        _createDialogOpen = true;
        try { dlg.ShowDialog(); }
        finally { _createDialogOpen = false; }
    }

    private void ToggleActive()
    {
        var list = GridKit.CheckedItems<Formula>(_grid);
        if (list.Count == 0 && GridKit.Current<Formula>(_grid) is { } row)
            list = new List<Formula> { row.Item };
        if (list.Count == 0) { MessageBox.Show("请先勾选配方"); return; }
        using var db = _dbFactory.CreateDbContext();
        var n = 0;
        foreach (var f in list)
        {
            var entity = db.Formulas.First(x => x.Id == f.Id);
            entity.IsActive = !entity.IsActive;
            _audit.Log("formula", $"{(entity.IsActive ? "启用" : "停用")}配方 {entity.Code}", _auth.CurrentUser?.Username ?? "");
            n++;
        }
        db.SaveChanges();
        Refresh();
        UiFeedback.Success($"已更新 {n} 个配方的启用状态。");
    }

    private Formula? CurrentFormula()
    {
        var checkedItems = GridKit.CheckedItems<Formula>(_grid);
        if (checkedItems.Count == 1) return checkedItems[0];
        return GridKit.Current<Formula>(_grid)?.Item;
    }

    private void CopySelected()
    {
        if (CurrentFormula() is not { } src)
        {
            MessageBox.Show("请先选择一条要复制的配方（勾选一条或单击选中）", "提示");
            return;
        }

        var nameBox = new TextBox
        {
            Text = FormulaClone.DefaultCopyName(src.Name),
            Margin = new Thickness(0, 4, 0, 10)
        };
        string previewCode;
        using (var db = _dbFactory.CreateDbContext())
        {
            var last = db.Formulas.OrderByDescending(f => f.Id).Select(f => (int?)f.Id).FirstOrDefault();
            previewCode = CodeService.PreviewNext(CodeKind.Formula, last);
        }

        var dlg = new Window
        {
            Title = "复制配方",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            MinHeight = 220,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(UiTheme.Hint($"源配方：{src.Code}  {src.Name}"));
        stack.Children.Add(new TextBlock { Text = "新名称 *", Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(nameBox);
        stack.Children.Add(UiTheme.Hint($"将自动分配编码（预览 {previewCode}），并复制全部称量步骤"));
        var ok = UiTheme.PrimaryButton("创建副本并编辑");
        ok.Margin = new Thickness(0, 12, 0, 0);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show(dlg, "请填写新名称");
                return;
            }
            try
            {
                var newId = _formulas.Clone(src.Id, nameBox.Text, _auth.CurrentUser?.DisplayName);
                using var db = _dbFactory.CreateDbContext();
                var created = db.Formulas.First(f => f.Id == newId);
                _audit.Log("formula", $"复制配方 {src.Code} → {created.Code}（{created.Name}）",
                    _auth.CurrentUser?.Username ?? "");
                dlg.DialogResult = true;
                dlg.Close();
                Refresh();
                UiFeedback.Success($"已创建 {created.Code}，可继续调整步骤。");
                _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(newId));
            }
            catch (Exception ex)
            {
                MessageBox.Show(dlg, ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        stack.Children.Add(ok);
        dlg.Content = stack;
        dlg.ShowDialog();
    }

    private void DeleteSelected()
    {
        var list = GridKit.CheckedItems<Formula>(_grid);
        if (list.Count == 0 && GridKit.Current<Formula>(_grid)?.Item is { } one) list = new List<Formula> { one };
        if (list.Count == 0) { MessageBox.Show("请先勾选要删除的配方"); return; }

        using var db = _dbFactory.CreateDbContext();
        var blocked = list.Where(f => db.WorkOrders.Any(w => w.FormulaId == f.Id)).ToList();
        if (blocked.Count > 0)
        {
            var lines = string.Join("\n", blocked.Select(f => $"· {f.Code}  {f.Name}"));
            MessageBox.Show(
                $"以下配方已有工单，无法删除（整批中止，未删除任何项）：\n{lines}",
                "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"确认删除 {list.Count} 个配方及其步骤？此操作不可恢复。", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var f in list)
        {
            var entity = db.Formulas.Include(x => x.Items).First(x => x.Id == f.Id);
            db.FormulaItems.RemoveRange(entity.Items);
            db.Formulas.Remove(entity);
            _audit.Log("formula", $"删除配方 {f.Code}", _auth.CurrentUser?.Username ?? "");
        }
        db.SaveChanges();
        Refresh();
        UiFeedback.Success($"已删除 {list.Count} 个配方。");
    }

    private static string ResolveCreateCode(MaterialMgmtDbContext db, bool isManual, string manualInput)
    {
        if (isManual)
        {
            return CodeService.AssertUnique(Array.Empty<string>(), manualInput, null,
                db.Formulas.Select(x => new ValueTuple<int, string>(x.Id, x.Code)).AsEnumerable());
        }

        var nextId = (db.Formulas.Select(x => (int?)x.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Formula, nextId,
            (c, _) => !db.Formulas.Any(x => x.Code == c));
    }
}

public sealed class BoolActiveConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? "启用" : "停用";
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
