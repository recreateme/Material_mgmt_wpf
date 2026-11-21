using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Export;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using MaterialMgmt.App.Views.Dialogs;

namespace MaterialMgmt.App.Views.Panels;

public sealed class AdminHistoryPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly OrderService _orders;
    private readonly ExportService _export;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new();
    private readonly PaginationBar _pager;
    private readonly EmptyState _empty;
    private WorkOrderFilterInputs _filters = null!;
    private List<Row> _all = new();
    private List<Row> _filtered = new();

    public AdminHistoryPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth, IAuditLog audit, OrderService orders, ExportService export, IConfigService config)
    {
        _dbFactory = dbFactory; _auth = auth; _audit = audit; _orders = orders; _export = export;
        _pager = new PaginationBar(config, "history");
        _empty = new EmptyState("暂无工单记录");
        Build();
        _empty.ClearFilter = _filters.Clear;
    }

    public void OnShow() => Refresh();

    public void OnBarcode(string code, bool simulated = false)
        => BarcodeFillHelper.FillFilter(_filter, code);

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new PageHeader("工单历史");
        DockPanel.SetDock(header, Dock.Top);
        _filters = new WorkOrderFilterInputs(header, _filter, "搜索配方、生产编号或操作员");
        _filters.Changed += () => { _pager.Reset(); ApplyFilter(); };
        var detail = header.AddAction(UiTheme.PrimaryButton("详情/异常说明")); detail.Click += (_, _) => ShowDetail();
        var export = header.AddAction(UiTheme.PrimaryButton("导出")); export.Click += (_, _) => ExportOne();
        var batch = header.AddAction(UiTheme.GhostButton("批量导出")); batch.Click += (_, _) => ExportBatch();
        var del = header.AddAction(UiTheme.DangerButton("删除")); del.Click += (_, _) => Delete();

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        GridKit.BindSingleActions(_grid, detail, export);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("配方", "Item.FormulaName", 180, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("操作员", "Item.Operator", 110, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("生产编号", "Item.ProductionNo", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.StatusColumn("状态", "Item.Status", "Item.StatusCn"));
        _grid.Columns.Add(GridKit.Text("开始时间", "Item.StartTimeDisplay", 150));
        _grid.Columns.Add(GridKit.Text("结束时间", "Item.EndTimeDisplay", 150));
        _grid.MouseDoubleClick += (_, _) => { if (GridKit.Current<Row>(_grid) is not null) ShowDetail(); };

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
        _all = db.WorkOrders.Include(w => w.Formula).Where(w => w.DeletedAt == null)
            .OrderByDescending(w => w.Id).AsEnumerable()
            .Select(w => new Row(w.Id, w.FormulaId, $"{w.Formula.Code} {w.Formula.Name}", w.Operator, w.ProductionNo,
                OrderRules.ToDisplay(w.Status), w.StartTime, w.EndTime, w.Status))
            .ToList();
        _filters.SetFormulas(_all.Select(r => (r.FormulaId, r.FormulaName)));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = _filters.Current;
        _filtered = _all.Where(r => f.Matches(r.Status, r.StartTime, r.FormulaId,
            r.FormulaName, r.ProductionNo, r.Operator)).ToList();
        _grid.ItemsSource = GridKit.Wrap(_pager.Apply(_filtered));
        _empty.Update(_filtered.Count, filtered: !f.IsEmpty);
    }

    private void ShowDetail()
    {
        if (CurrentOrder() is not { } r) { UiFeedback.Warn("请先选择工单。"); return; }
        var dlg = new WorkOrderDetailDialog(_dbFactory, _audit, _auth.CurrentUser?.Username ?? "", r.Id, r.FormulaName)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            UiFeedback.Success($"工单 #{r.Id} 的异常说明已保存。");
    }

    private void ExportOne()
    {
        if (CurrentOrder() is not { } r) { UiFeedback.Hint("请先选择工单"); return; }
        var dlg = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = ExportService.SuggestFilename(r.Id, r.FormulaName) };
        if (dlg.ShowDialog() != true) return;
        UiFeedback.Export(dlg.FileName, () => _export.ExportWorkOrder(r.Id, dlg.FileName));
    }

    private void ExportBatch()
    {
        var ids = CheckedOrderIds();
        if (ids.Count == 0) { UiFeedback.Hint("请勾选要导出的工单"); return; }
        var dlg = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = ExportService.SuggestBatchFilename() };
        if (dlg.ShowDialog() != true) return;
        UiFeedback.Export(dlg.FileName, () => _export.ExportWorkOrders(ids, dlg.FileName));
    }

    private void Delete()
    {
        var rows = SelectedAdminRows();
        if (rows.Count == 0) { UiFeedback.Warn("请勾选要删除的工单。"); return; }
        var targets = rows.Where(r => r.Status is "completed" or "cancelled").ToList();
        if (targets.Count == 0)
        {
            UiFeedback.Warn("仅可删除「已完成 / 已取消」的工单；进行中请先取消。");
            return;
        }
        var skipped = rows.Count - targets.Count;
        var tip = skipped > 0
            ? $"将删除 {targets.Count} 条已完成/已取消工单（已跳过 {skipped} 条进行中）？"
            : $"确认删除 {targets.Count} 条工单？此操作不可恢复。";
        if (!ConfirmDialog.Ask(tip, "确认删除", "删除", danger: true)) return;
        try
        {
            var ids = targets.Select(r => r.Id).ToList();
            _orders.DeleteMany(ids, allowCompleted: true, deletedBy: _auth.CurrentUser?.Username);
            _audit.Log("order", $"删除工单 {string.Join(",", ids)}", _auth.CurrentUser?.Username ?? "");
            Refresh();
            UiFeedback.Success($"已删除 {targets.Count} 条工单。");
        }
        catch (Exception ex) { UiFeedback.Warn(ex.Message); }
    }

    private Row? CurrentOrder() => GridKit.Current<Row>(_grid)?.Item
                                  ?? GridKit.CheckedItems<Row>(_grid).FirstOrDefault();

    private List<int> CheckedOrderIds() => SelectedAdminRows().Select(x => x.Id).ToList();

    private List<Row> SelectedAdminRows()
    {
        var list = GridKit.CheckedItems<Row>(_grid);
        if (list.Count == 0 && CurrentOrder() is { } one) list = new List<Row> { one };
        return list;
    }

    private sealed record Row(
        int Id, int FormulaId, string FormulaName, string Operator, string? ProductionNo,
        string StatusCn, DateTime StartTime, DateTime? EndTime, string Status)
    {
        public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm");
        public string EndTimeDisplay => EndTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    }
}
