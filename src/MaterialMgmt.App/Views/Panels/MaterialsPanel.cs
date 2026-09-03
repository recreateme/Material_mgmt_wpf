using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class MaterialsPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly DeviceHub _devices;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new() { Width = 220, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
    private readonly PaginationBar _pager = new(10);
    private List<Material> _all = new();
    private List<Material> _filtered = new();
    private bool _editDialogOpen;

    public MaterialsPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth, IAuditLog audit,
        DeviceHub devices)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _devices = devices;
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
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(toolbar, Dock.Top);

        toolbar.Children.Add(new TextBlock
        {
            Text = "原材料", FontSize = 20, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
        });
        toolbar.Children.Add(_filter);
        _filter.TextChanged += (_, _) => { _pager.Reset(); ApplyFilter(); };

        var addBtn = UiTheme.PrimaryButton("新增");
        addBtn.Click += (_, _) => Edit(null);
        toolbar.Children.Add(addBtn);

        var delBtn = UiTheme.DangerButton("删除选中");
        delBtn.Click += (_, _) => DeleteSelected();
        toolbar.Children.Add(delBtn);
        GridKit.AddSelectButtons(toolbar, _grid);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("编码", "Item.Code", 140));
        _grid.Columns.Add(GridKit.Text("名称", "Item.Name", 200));
        _grid.Columns.Add(GridKit.Text("描述", "Item.Description", new DataGridLength(1, DataGridLengthUnitType.Star)));
        _grid.Columns.Add(GridKit.Text("创建时间", "Item.CreatedAt", 160, "yyyy-MM-dd HH:mm"));
        _grid.MouseDoubleClick += (_, _) =>
        {
            if (GridKit.Current<Material>(_grid) is { } row) Edit(row.Item);
        };

        _pager.PageChanged += ApplyFilter;
        DockPanel.SetDock(_pager, Dock.Bottom);

        root.Children.Add(toolbar);
        root.Children.Add(_pager);
        root.Children.Add(_grid);
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
            MessageBox.Show("请先勾选要删除的材料", "提示");
            return;
        }
        if (MessageBox.Show($"确认删除 {selected.Count} 条原材料？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        using var db = _dbFactory.CreateDbContext();
        foreach (var m in selected)
        {
            var entity = db.Materials.FirstOrDefault(x => x.Id == m.Id);
            if (entity is null) continue;
            if (db.FormulaItems.Any(i => i.MaterialId == entity.Id))
            {
                MessageBox.Show($"材料 {entity.Code} 已被配方引用，无法删除", "失败");
                return;
            }
            db.Materials.Remove(entity);
            _audit.Log("material", $"删除材料 {entity.Code}", _auth.CurrentUser?.Username ?? "");
        }
        db.SaveChanges();
        UiFeedback.Success($"已删除 {selected.Count} 条原材料。");
        Refresh();
    }
}

internal sealed class MaterialEditDialog : Window
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly Material? _existing;
    private readonly TextBox _name = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox _desc = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10) };
    private readonly CheckBox _manual = new() { Content = "手工指定编码", Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _code = new() { Height = 30, Margin = new Thickness(0, 4, 0, 10), IsEnabled = false };
    public string SavedCode { get; private set; } = "";

    public MaterialEditDialog(IDbContextFactory<MaterialMgmtDbContext> dbFactory, DeviceHub devices, Material? existing)
    {
        _dbFactory = dbFactory;
        _existing = existing;
        Title = existing is null ? "新增原材料" : "编辑原材料";
        Width = 420; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        using (var db = _dbFactory.CreateDbContext())
        {
            var last = db.Materials.OrderByDescending(m => m.Id).Select(m => (int?)m.Id).FirstOrDefault();
            _code.Text = existing?.Code ?? CodeService.PreviewNext(CodeKind.Material, last);
        }

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _desc.Text = existing.Description ?? "";
            _code.Text = existing.Code;
        }

        _manual.Checked += (_, _) => _code.IsEnabled = true;
        _manual.Unchecked += (_, _) =>
        {
            _code.IsEnabled = false;
            using var db = _dbFactory.CreateDbContext();
            var last = db.Materials.OrderByDescending(m => m.Id).Select(m => (int?)m.Id).FirstOrDefault();
            if (_existing is null) _code.Text = CodeService.PreviewNext(CodeKind.Material, last);
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "名称 *" });
        stack.Children.Add(_name);
        stack.Children.Add(new TextBlock { Text = "描述" });
        stack.Children.Add(_desc);
        stack.Children.Add(_manual);
        stack.Children.Add(new TextBlock { Text = "编码（可扫码填入）" });
        stack.Children.Add(_code);
        var save = UiTheme.PrimaryButton("保存");
        save.Height = 34;
        save.Margin = new Thickness(0, 8, 0, 0);
        save.Click += (_, _) => Save();
        stack.Children.Add(save);
        Content = stack;

        BarcodeFillHelper.AttachDialog(this, devices, _manual, _code);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show(this, "请填写名称", "提示");
            return;
        }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            Material entity;
            if (_existing is null)
            {
                var code = ResolveCreateCode(db);
                entity = new Material
                {
                    Name = _name.Text.Trim(),
                    Description = string.IsNullOrWhiteSpace(_desc.Text) ? null : _desc.Text.Trim(),
                    CreatedAt = DateTime.Now,
                    Code = code
                };
                db.Materials.Add(entity);
            }
            else
            {
                entity = db.Materials.First(m => m.Id == _existing.Id);
                entity.Name = _name.Text.Trim();
                entity.Description = string.IsNullOrWhiteSpace(_desc.Text) ? null : _desc.Text.Trim();
                if (_manual.IsChecked == true)
                {
                    entity.Code = CodeService.AssertUnique(
                        Array.Empty<string>(),
                        _code.Text,
                        entity.Id,
                        db.Materials.Select(m => new ValueTuple<int, string>(m.Id, m.Code)).AsEnumerable());
                }
            }
            db.SaveChanges();
            SavedCode = entity.Code;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ResolveErrorMessage(ex), "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string ResolveCreateCode(MaterialMgmtDbContext db)
    {
        if (_manual.IsChecked == true)
        {
            return CodeService.AssertUnique(
                Array.Empty<string>(),
                _code.Text,
                null,
                db.Materials.Select(m => new ValueTuple<int, string>(m.Id, m.Code)).AsEnumerable());
        }

        var nextId = (db.Materials.Select(m => (int?)m.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Material, nextId,
            (c, _) => !db.Materials.Any(m => m.Code == c));
    }

    private static string ResolveErrorMessage(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null) e = e.InnerException;
        return e.Message;
    }
}
