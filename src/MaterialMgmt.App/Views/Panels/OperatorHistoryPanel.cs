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

/// <summary>我的工单（称量窗内）：只列当前业务人员自己的工单。</summary>
public sealed class OperatorHistoryPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly OrderService _orders;
    private readonly ExportService _export;
    private readonly NavigationService _nav;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new();
    private readonly PaginationBar _pager;
    private readonly EmptyState _empty;
    private WorkOrderFilterInputs _filters = null!;
    private List<Row> _all = new();

    public OperatorHistoryPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth, IAuditLog audit, OrderService orders,
        ExportService export, NavigationService nav, IConfigService config)
    {
        _dbFactory = dbFactory; _auth = auth; _audit = audit;
        _orders = orders; _export = export; _nav = nav;
        _pager = new PaginationBar(config, "my_orders");
        _empty = new EmptyState("你还没有工单。在「开始称量」里选择配方即可开始。", "开始称量",
            () => _nav.Navigate(AppPage.SessionSelect));
        Build();
        _empty.ClearFilter = _filters.Clear;
    }

    public void OnShow() => Refresh();

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new PageHeader("我的工单");
        DockPanel.SetDock(bar, Dock.Top);
        _filters = new WorkOrderFilterInputs(bar, _filter, "搜索配方或生产编号");
        _filters.Changed += () => { _pager.Reset(); Render(); };
        var resume = bar.AddAction(UiTheme.PrimaryButton("继续称量")); resume.Click += (_, _) => Resume();
        var cancel = bar.AddAction(UiTheme.GhostButton("取消进行中")); cancel.Click += (_, _) => CancelSelected();
        var del = bar.AddAction(UiTheme.DangerButton("删除已完成")); del.Click += (_, _) => DeleteCompleted();
        var export = bar.AddAction(UiTheme.PrimaryButton("导出")); export.Click += (_, _) => ExportOne();
        var batch = bar.AddAction(UiTheme.GhostButton("批量导出")); batch.Click += (_, _) => ExportBatch();

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        GridKit.BindSingleActions(_grid, resume, export);
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("配方", "Item.FormulaName", 180, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("生产编号", "Item.ProductionNo", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.StatusColumn("状态", "Item.Status", "Item.StatusCn"));
        _grid.Columns.Add(GridKit.Text("开始时间", "Item.StartTimeDisplay", 150));
        _grid.Columns.Add(GridKit.Text("结束时间", "Item.EndTimeDisplay", 150));

        _pager.PageChanged += Render;
        DockPanel.SetDock(_pager, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_pager);
        root.Children.Add(EmptyState.Host(_grid, _empty));
        Content = root;
    }

    private void Refresh()
    {
        var uid = _auth.CurrentUser!.Id;
        using var db = _dbFactory.CreateDbContext();
        _all = db.WorkOrders.Include(w => w.Formula)
            .Where(w => w.OperatorId == uid && w.DeletedAt == null)
            .OrderByDescending(w => w.Id)
            .AsEnumerable()
            .Select(w => new Row(w.Id, w.FormulaId, $"{w.Formula.Code} {w.Formula.Name}", w.ProductionNo,
                OrderRules.ToDisplay(w.Status), w.StartTime, w.EndTime, w.Status))
            .ToList();
        _filters.SetFormulas(_all.Select(r => (r.FormulaId, r.FormulaName)));
        Render();
    }

    private void Render()
    {
        var f = _filters.Current;
        var shown = _all.Where(r => f.Matches(r.Status, r.StartTime, r.FormulaId, r.FormulaName, r.ProductionNo)).ToList();
        _grid.ItemsSource = GridKit.Wrap(_pager.Apply(shown));
        _empty.Update(shown.Count, filtered: !f.IsEmpty);
    }

    private void Resume()
    {
        if (CurrentOrder() is not { } r) { MessageBox.Show("请先选择工单", "提示"); return; }
        if (r.Status != "in_progress") { MessageBox.Show("仅可恢复进行中的工单"); return; }
        _nav.Navigate(AppPage.WeighSession, v => ((WeighSessionPanel)v).Load(r.Id, r.FormulaId));
    }

    private void CancelSelected()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { MessageBox.Show("请先勾选要进行中的工单"); return; }
        var targets = rows.Where(r => r.Status == "in_progress").ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("仅可取消「进行中」的工单；已完成请使用「删除已完成」");
            return;
        }
        var skipped = rows.Count - targets.Count;
        var tip = skipped > 0
            ? $"将取消 {targets.Count} 条进行中工单（已跳过 {skipped} 条非进行中）？"
            : $"确认取消 {targets.Count} 条进行中工单？";
        if (!ConfirmDialog.Ask(tip, "确认取消", "取消工单", "返回", danger: true))
            return;
        try
        {
            foreach (var r in targets)
            {
                _orders.Cancel(r.Id);
                _audit.Log("order", $"取消工单 #{r.Id}", _auth.CurrentUser?.Username ?? "");
            }
            Refresh();
            UiFeedback.Success(targets.Count == 1
                ? $"工单 #{targets[0].Id} 已取消。"
                : $"已取消 {targets.Count} 条工单。");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void DeleteCompleted()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { MessageBox.Show("请先勾选要删除的已完成工单"); return; }
        var targets = rows.Where(r => r.Status is "completed" or "cancelled").ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("仅可删除「已完成 / 已取消」的工单；进行中请先取消");
            return;
        }
        var skipped = rows.Count - targets.Count;
        var tip = skipped > 0
            ? $"将删除 {targets.Count} 条已完成/已取消工单（已跳过 {skipped} 条进行中）？"
            : $"确认删除 {targets.Count} 条工单？此操作不可恢复。";
        if (!ConfirmDialog.Ask(tip, "确认删除", "删除", danger: true))
            return;
        try
        {
            var ids = targets.Select(r => r.Id).ToList();
            _orders.DeleteMany(ids, allowCompleted: true, deletedBy: _auth.CurrentUser?.Username);
            _audit.Log("order", $"操作员删除工单 {string.Join(",", ids)}", _auth.CurrentUser?.Username ?? "");
            Refresh();
            UiFeedback.Success($"已删除 {targets.Count} 条工单。");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void ExportOne()
    {
        if (CurrentOrder() is not { } r) { MessageBox.Show("请先选择工单", "提示"); return; }
        var dlg = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = ExportService.SuggestFilename(r.Id, r.FormulaName) };
        if (dlg.ShowDialog() != true) return;
        _export.ExportWorkOrder(r.Id, dlg.FileName);
        MessageBox.Show("导出完成");
    }

    private void ExportBatch()
    {
        var ids = CheckedOrderIds();
        if (ids.Count == 0) { MessageBox.Show("请勾选要导出的工单"); return; }
        var dlg = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = ExportService.SuggestBatchFilename() };
        if (dlg.ShowDialog() != true) return;
        _export.ExportWorkOrders(ids, dlg.FileName);
        MessageBox.Show("导出完成");
    }

    private List<Row> SelectedRows()
    {
        var list = GridKit.CheckedItems<Row>(_grid);
        if (list.Count == 0 && CurrentOrder() is { } one) list = new List<Row> { one };
        return list;
    }

    private Row? CurrentOrder() => GridKit.Current<Row>(_grid)?.Item
                                  ?? GridKit.CheckedItems<Row>(_grid).FirstOrDefault();

    private List<int> CheckedOrderIds() => SelectedRows().Select(x => x.Id).ToList();

    private sealed record Row(
        int Id, int FormulaId, string FormulaName, string? ProductionNo,
        string StatusCn, DateTime StartTime, DateTime? EndTime, string Status)
    {
        public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm");
        public string EndTimeDisplay => EndTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    }
}
