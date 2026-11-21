using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Services;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class FormulasPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly FormulaService _formulas;
    private readonly NavigationService _nav;
    private readonly DeviceHub _devices;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new();
    private readonly PaginationBar _pager;
    private readonly EmptyState _empty;
    private List<Formula> _all = new();
    private List<Formula> _filtered = new();
    private bool _createDialogOpen;

    public FormulasPanel(IAuthService auth,
        IAuditLog audit, FormulaService formulas, NavigationService nav, DeviceHub devices, IConfigService config)
    {
        _auth = auth;
        _audit = audit;
        _formulas = formulas;
        _nav = nav;
        _devices = devices;
        _pager = new PaginationBar(config, "formulas");
        _empty = new EmptyState("暂无配方", "新建配方", Create) { ClearFilter = () => _filter.Text = "" };
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
        var header = new PageHeader("配方管理");
        DockPanel.SetDock(header, Dock.Top);
        header.AddSearch(_filter, "搜索编码或名称，可扫码");
        _filter.TextChanged += (_, _) => { _pager.Reset(); Render(); };

        var add = header.AddAction(UiTheme.PrimaryButton("新建配方"));
        add.Click += (_, _) => Create();
        var edit = header.AddAction(UiTheme.PrimaryButton("编辑步骤"));
        edit.Click += (_, _) =>
        {
            if (CurrentFormula() is { } row)
                _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(row.Id));
            else
                UiFeedback.Warn("请先选择一条配方。");
        };
        var copy = header.AddAction(UiTheme.GhostButton("复制配方"));
        copy.Click += (_, _) => CopySelected();
        var toggle = header.AddAction(UiTheme.GhostButton("启用/停用"));
        toggle.Click += (_, _) => ToggleActive();
        var del = header.AddAction(UiTheme.DangerButton("删除选中"));
        del.Click += (_, _) => DeleteSelected();

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
        root.Children.Add(header);
        root.Children.Add(_pager);
        root.Children.Add(EmptyState.Host(_grid, _empty));
        Content = root;
    }

    private void Refresh()
    {
        _all = _formulas.All().OrderByDescending(f => f.Id).ToList();
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
        _empty.Update(_filtered.Count, filtered: q.Length > 0);
    }

    private void Create()
    {
        var dlg = new FormulaCreateDialog(_formulas, _devices, _auth.CurrentUser?.DisplayName)
        { Owner = Window.GetWindow(this) };
        _createDialogOpen = true;
        try
        {
            if (dlg.ShowDialog() != true) return;
        }
        finally { _createDialogOpen = false; }

        _audit.Log("formula", $"新建配方 {dlg.CreatedCode}", _auth.CurrentUser?.Username ?? "");
        Refresh();
        _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(dlg.CreatedId));
    }

    private void ToggleActive()
    {
        var list = GridKit.CheckedItems<Formula>(_grid);
        if (list.Count == 0 && GridKit.Current<Formula>(_grid) is { } row)
            list = new List<Formula> { row.Item };
        if (list.Count == 0) { UiFeedback.Hint("请先勾选配方"); return; }
        var changed = _formulas.ToggleActive(list.Select(f => f.Id));
        foreach (var f in changed)
            _audit.Log("formula", $"{(f.IsActive ? "启用" : "停用")}配方 {f.Code}", _auth.CurrentUser?.Username ?? "");
        Refresh();
        UiFeedback.Success($"已更新 {changed.Count} 个配方的启用状态。");
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
            UiFeedback.Warn("请先选择一条要复制的配方（勾选一条或单击选中）。");
            return;
        }

        var dlg = new FormulaCopyDialog(_formulas, src, _auth.CurrentUser?.DisplayName)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        _audit.Log("formula", $"复制配方 {src.Code} → {dlg.CreatedCode}（{dlg.CreatedName}）",
            _auth.CurrentUser?.Username ?? "");
        Refresh();
        UiFeedback.Success($"已创建 {dlg.CreatedCode}，可继续调整步骤。");
        _nav.Navigate(AppPage.FormulaEditor, v => ((FormulaEditorPanel)v).Load(dlg.CreatedId));
    }

    private void DeleteSelected()
    {
        var list = GridKit.CheckedItems<Formula>(_grid);
        if (list.Count == 0 && GridKit.Current<Formula>(_grid)?.Item is { } one) list = new List<Formula> { one };
        if (list.Count == 0) { UiFeedback.Hint("请先勾选要删除的配方"); return; }

        var blocked = _formulas.WithWorkOrders(list.Select(f => f.Id));
        if (blocked.Count > 0)
        {
            var lines = string.Join("\n", blocked.Select(f => $"· {f.Code}  {f.Name}"));
            UiFeedback.Warn($"以下配方已有工单，无法删除（整批中止，未删除任何项）：\n{lines}", "无法删除");
            return;
        }

        if (!ConfirmDialog.Ask($"确认删除 {list.Count} 个配方及其步骤？此操作不可恢复。", "确认删除", "删除", danger: true))
            return;

        try
        {
            var deleted = _formulas.DeleteMany(list.Select(f => f.Id));
            foreach (var f in deleted)
                _audit.Log("formula", $"删除配方 {f.Code}", _auth.CurrentUser?.Username ?? "");
            UiFeedback.Success($"已删除 {deleted.Count} 个配方。");
        }
        catch (Exception ex)
        {
            UiFeedback.Warn(ex.GetBaseException().Message, "无法删除");
        }
        Refresh();
    }
}

public sealed class BoolActiveConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? "启用" : "停用";
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
