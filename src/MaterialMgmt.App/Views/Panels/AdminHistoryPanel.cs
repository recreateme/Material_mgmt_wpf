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

namespace MaterialMgmt.App.Views.Panels;

public sealed class OperatorHistoryPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly OrderService _orders;
    private readonly ExportService _export;
    private readonly NavigationService _nav;
    private readonly DataGrid _grid = new();
    private readonly PaginationBar _pager = new(10);
    private List<Row> _all = new();

    public OperatorHistoryPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth, IAuditLog audit, OrderService orders,
        ExportService export, NavigationService nav)
    {
        _dbFactory = dbFactory; _auth = auth; _audit = audit;
        _orders = orders; _export = export; _nav = nav;
        Build();
    }

    public void OnShow() => Refresh();

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        bar.Children.Add(new TextBlock { Text = "我的工单", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        var resume = UiTheme.PrimaryButton("继续称量"); resume.Click += (_, _) => Resume(); bar.Children.Add(resume);
        var cancel = UiTheme.GhostButton("取消进行中"); cancel.Click += (_, _) => CancelSelected(); bar.Children.Add(cancel);
        var del = UiTheme.DangerButton("删除已完成"); del.Click += (_, _) => DeleteCompleted(); bar.Children.Add(del);
        var export = UiTheme.PrimaryButton("导出"); export.Click += (_, _) => ExportOne(); bar.Children.Add(export);
        var batch = UiTheme.GhostButton("批量导出"); batch.Click += (_, _) => ExportBatch(); bar.Children.Add(batch);
        GridKit.AddSelectButtons(bar, _grid);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true; _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("配方", "Item.FormulaName", 180));
        _grid.Columns.Add(GridKit.Text("生产编号", "Item.ProductionNo", 140));
        _grid.Columns.Add(GridKit.Text("状态", "Item.StatusCn", 90));
        _grid.Columns.Add(GridKit.Text("开始时间", "Item.StartTimeDisplay", 150));
        _grid.Columns.Add(GridKit.Text("结束时间", "Item.EndTimeDisplay", 150));

        _pager.PageChanged += Render;
        DockPanel.SetDock(_pager, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_pager);
        root.Children.Add(_grid);
        Content = root;
    }

    private void Refresh()
    {
        var uid = _auth.CurrentUser!.Id;
        using var db = _dbFactory.CreateDbContext();
        _all = db.WorkOrders.Include(w => w.Formula)
            .Where(w => w.OperatorId == uid)
            .OrderByDescending(w => w.Id)
            .AsEnumerable()
            .Select(w => new Row(w.Id, w.FormulaId, $"{w.Formula.Code} {w.Formula.Name}", w.ProductionNo,
                OrderRules.ToDisplay(w.Status), w.StartTime, w.EndTime, w.Status))
            .ToList();
        _pager.Reset();
        Render();
    }

    private void Render() => _grid.ItemsSource = GridKit.Wrap(_pager.Apply(_all));

    private void Resume()
    {
        if (CurrentOrder() is not { } r) return;
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
        if (MessageBox.Show(tip, "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            foreach (var r in targets)
            {
                _orders.Cancel(r.Id);
                _audit.Log("order", $"取消工单 #{r.Id}", _auth.CurrentUser?.Username ?? "");
            }
            Refresh();
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
        if (MessageBox.Show(tip, "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var ids = targets.Select(r => r.Id).ToList();
            _orders.DeleteMany(ids, allowCompleted: true);
            _audit.Log("order", $"操作员删除工单 {string.Join(",", ids)}", _auth.CurrentUser?.Username ?? "");
            Refresh();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void ExportOne()
    {
        if (CurrentOrder() is not { } r) return;
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

public sealed class AdminHistoryPanel : UserControl, IPageLifecycle
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly OrderService _orders;
    private readonly ExportService _export;
    private readonly DataGrid _grid = new();
    private readonly TextBox _filter = new() { Width = 200, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
    private readonly PaginationBar _pager = new(10);
    private List<Row> _all = new();
    private List<Row> _filtered = new();

    public AdminHistoryPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth, IAuditLog audit, OrderService orders, ExportService export)
    {
        _dbFactory = dbFactory; _auth = auth; _audit = audit; _orders = orders; _export = export;
        Build();
    }

    public void OnShow() => Refresh();

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        bar.Children.Add(new TextBlock { Text = "工单历史", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_filter);
        _filter.TextChanged += (_, _) => { _pager.Reset(); ApplyFilter(); };
        var detail = UiTheme.PrimaryButton("详情/异常说明"); detail.Click += (_, _) => ShowDetail(); bar.Children.Add(detail);
        var export = UiTheme.PrimaryButton("导出"); export.Click += (_, _) => ExportOne(); bar.Children.Add(export);
        var batch = UiTheme.GhostButton("批量导出"); batch.Click += (_, _) => ExportBatch(); bar.Children.Add(batch);
        var del = UiTheme.DangerButton("删除"); del.Click += (_, _) => Delete(); bar.Children.Add(del);
        GridKit.AddSelectButtons(bar, _grid);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true; _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.Columns.Add(GridKit.CheckColumn());
        _grid.Columns.Add(GridKit.Text("ID", "Item.Id", 70));
        _grid.Columns.Add(GridKit.Text("配方", "Item.FormulaName", 180));
        _grid.Columns.Add(GridKit.Text("操作员", "Item.Operator", 110));
        _grid.Columns.Add(GridKit.Text("生产编号", "Item.ProductionNo", 140));
        _grid.Columns.Add(GridKit.Text("状态", "Item.StatusCn", 90));
        _grid.Columns.Add(GridKit.Text("开始时间", "Item.StartTimeDisplay", 150));
        _grid.Columns.Add(GridKit.Text("结束时间", "Item.EndTimeDisplay", 150));

        _pager.PageChanged += ApplyFilter;
        DockPanel.SetDock(_pager, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_pager);
        root.Children.Add(_grid);
        Content = root;
    }

    private void Refresh()
    {
        using var db = _dbFactory.CreateDbContext();
        _all = db.WorkOrders.Include(w => w.Formula).OrderByDescending(w => w.Id).AsEnumerable()
            .Select(w => new Row(w.Id, $"{w.Formula.Code} {w.Formula.Name}", w.Operator, w.ProductionNo,
                OrderRules.ToDisplay(w.Status), w.StartTime, w.EndTime))
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = _filter.Text.Trim();
        _filtered = string.IsNullOrEmpty(q) ? _all :
            _all.Where(r => r.FormulaName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            (r.ProductionNo?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            r.Operator.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.ItemsSource = GridKit.Wrap(_pager.Apply(_filtered));
    }

    private void ShowDetail()
    {
        if (CurrentOrder() is not { } r) return;
        using var db = _dbFactory.CreateDbContext();
        var wo = db.WorkOrders.Include(w => w.Records).ThenInclude(x => x.FormulaItem).ThenInclude(i => i.Material)
            .First(w => w.Id == r.Id);

        var mix = new CheckBox { Content = "混料异常", IsChecked = wo.MixingException == true };
        var mixNote = new TextBox { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = wo.MixingExceptionNote ?? "", IsEnabled = mix.IsChecked == true };
        var coat = new CheckBox { Content = "涂布异常", IsChecked = wo.CoatingException == true };
        var coatNote = new TextBox { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = wo.CoatingExceptionNote ?? "", IsEnabled = coat.IsChecked == true };
        mix.Checked += (_, _) => mixNote.IsEnabled = true;
        mix.Unchecked += (_, _) => mixNote.IsEnabled = false;
        coat.Checked += (_, _) => coatNote.IsEnabled = true;
        coat.Unchecked += (_, _) => coatNote.IsEnabled = false;

        var info = new TextBlock
        {
            Text = $"工单 #{wo.Id}\n配方：{r.FormulaName}\n操作员：{wo.Operator}\n生产编号：{wo.ProductionNo}\n" +
                   $"温度：{wo.Temperature}  湿度：{wo.Humidity}\n基膜：{wo.BaseFilm}  批次：{wo.BatchNo}\n" +
                   string.Join("\n", wo.Records.OrderBy(x => x.Timestamp).Select(x =>
                       $"· {x.FormulaItem.Material.Name}: {x.ActualWeight:F3}g")),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };

        var dlg = new Window { Title = "工单详情", Width = 520, Height = 560, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(info); stack.Children.Add(mix); stack.Children.Add(mixNote);
        stack.Children.Add(coat); stack.Children.Add(coatNote);
        var save = UiTheme.PrimaryButton("保存异常说明");
        save.Click += (_, _) =>
        {
            using var db2 = _dbFactory.CreateDbContext();
            var e = db2.WorkOrders.First(x => x.Id == wo.Id);
            e.MixingException = mix.IsChecked == true;
            e.MixingExceptionNote = mix.IsChecked == true ? mixNote.Text.Trim() : null;
            e.CoatingException = coat.IsChecked == true;
            e.CoatingExceptionNote = coat.IsChecked == true ? coatNote.Text.Trim() : null;
            db2.SaveChanges();
            _audit.Log("order", $"更新异常说明 工单#{wo.Id}", _auth.CurrentUser?.Username ?? "");
            MessageBox.Show(dlg, "已保存");
            dlg.Close();
        };
        stack.Children.Add(save);
        dlg.Content = new ScrollViewer { Content = stack };
        dlg.ShowDialog();
    }

    private void ExportOne()
    {
        if (CurrentOrder() is not { } r) return;
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

    private void Delete()
    {
        var ids = CheckedOrderIds();
        if (ids.Count == 0) { MessageBox.Show("请勾选要删除的工单"); return; }
        if (MessageBox.Show($"确认删除 {ids.Count} 条工单？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _orders.DeleteMany(ids, allowCompleted: true);
            _audit.Log("order", $"删除工单 {string.Join(",", ids)}", _auth.CurrentUser?.Username ?? "");
            Refresh();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private Row? CurrentOrder() => GridKit.Current<Row>(_grid)?.Item
                                  ?? GridKit.CheckedItems<Row>(_grid).FirstOrDefault();

    private List<int> CheckedOrderIds()
    {
        var list = GridKit.CheckedItems<Row>(_grid);
        if (list.Count == 0 && CurrentOrder() is { } one) list = new List<Row> { one };
        return list.Select(x => x.Id).ToList();
    }

    private sealed record Row(
        int Id, string FormulaName, string Operator, string? ProductionNo,
        string StatusCn, DateTime StartTime, DateTime? EndTime)
    {
        public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm");
        public string EndTimeDisplay => EndTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    }
}
