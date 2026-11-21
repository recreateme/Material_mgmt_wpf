using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class MaterialsPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly DeviceHub _devices;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new();
    private readonly PaginationBar _pager;
    private readonly EmptyState _empty;
    private List<Material> _all = new();
    private List<Material> _filtered = new();
    private bool _editDialogOpen;

    public MaterialsPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit,
        DeviceHub devices, IConfigService config)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _devices = devices;
        _pager = new PaginationBar(config, "materials");
        _empty = new EmptyState("暂无原材料", "新增原材料", () => Edit(null)) { ClearFilter = () => _filter.Text = "" };
        Build();
    }

    public void OnShow() => Refresh();

    public void OnBarcode(string code, bool simulated = false)
    {
        if (_editDialogOpen) return;
        BarcodeFillHelper.FillFilter(_filter, code);
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new PageHeader("原材料");
        DockPanel.SetDock(header, Dock.Top);
        header.AddSearch(_filter, "搜索编码或名称，可扫码");
        _filter.TextChanged += (_, _) => { _pager.Reset(); ApplyFilter(); };

        var addBtn = header.AddAction(UiTheme.PrimaryButton("新增"));
        addBtn.Click += (_, _) => Edit(null);
        var delBtn = header.AddAction(UiTheme.DangerButton("删除选中"));
        delBtn.Click += (_, _) => DeleteSelected();

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        GridKit.EnableLinkedSelection(_grid);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("编码", "Item.Code", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("名称", "Item.Name", 200, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("描述", "Item.Description", new DataGridLength(1, DataGridLengthUnitType.Star)));
        _grid.Columns.Add(GridKit.Text("创建时间", "Item.CreatedAt", 160, "yyyy-MM-dd HH:mm"));
        _grid.MouseDoubleClick += (_, _) =>
        {
            if (GridKit.Current<Material>(_grid) is { } row) Edit(row.Item);
        };

        _pager.PageChanged += ApplyFilter;
        DockPanel.SetDock(_pager, Dock.Bottom);

        root.Children.Add(header);
        root.Children.Add(_pager);
        root.Children.Add(EmptyState.Host(_grid, _empty));
        Content = root;
    }

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        _all = db.Materials.OrderByDescending(m => m.Id).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = _filter.Text.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(m =>
                m.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.ItemsSource = GridKit.Wrap(_pager.Apply(_filtered));
        _empty.Update(_filtered.Count, filtered: q.Length > 0);
    }

    private void Edit(Material? existing)
    {
        var dlg = new MaterialEditDialog(_dbFactory, _devices, existing) { Owner = Window.GetWindow(this) };
        _editDialogOpen = true;
        try
        {
            if (dlg.ShowDialog() == true)
            {
                _audit.Log("material", existing is null ? $"新增材料 {dlg.SavedCode}" : $"编辑材料 {dlg.SavedCode}",
                    _auth.CurrentUser?.Username ?? "");
                Refresh();
                UiFeedback.Success(existing is null ? $"已新增原材料 {dlg.SavedCode}。" : "已保存。");
            }
        }
        finally
        {
            _editDialogOpen = false;
        }
    }

    private void DeleteSelected()
    {
        var selected = GridKit.CheckedItems<Material>(_grid);
        if (selected.Count == 0 && GridKit.Current<Material>(_grid)?.Item is { } one)
            selected = new List<Material> { one };
        if (selected.Count == 0)
        {
            UiFeedback.Warn("请先勾选要删除的材料。");
            return;
        }
        if (!ConfirmDialog.Ask($"确认删除 {selected.Count} 条原材料？", "确认删除", "删除", danger: true))
            return;

        using var db = _dbFactory.CreateDbContext();
        var blocked = selected.Where(m => db.FormulaItems.Any(i => i.MaterialId == m.Id)).ToList();
        if (blocked.Count > 0)
        {
            var lines = string.Join("\n", blocked.Select(m => $"· {m.Code}  {m.Name}"));
            MessageBox.Show(
                $"以下原材料已被配方引用，无法删除（整批中止，未删除任何项）：\n{lines}",
                "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var m in selected)
        {
            var entity = db.Materials.FirstOrDefault(x => x.Id == m.Id);
            if (entity is null) continue;
            db.Materials.Remove(entity);
            _audit.Log("material", $"删除材料 {entity.Code}", _auth.CurrentUser?.Username ?? "");
        }
        db.SaveChanges();
        UiFeedback.Success($"已删除 {selected.Count} 条原材料。");
        Refresh();
    }
}
