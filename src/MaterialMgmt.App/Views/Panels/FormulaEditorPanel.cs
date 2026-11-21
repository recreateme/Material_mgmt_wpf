using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
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
    private readonly OrderService _orders;
    private readonly DataGrid _grid = new();
    /// <summary>步骤操作按钮的容器：配方被进行中工单引用时整体禁用。</summary>
    private readonly WrapPanel _stepActions = new() { Orientation = Orientation.Horizontal };
    private readonly Border _lockBanner = new()
    {
        Background = UiTheme.Brush(UiTheme.WarningSoft),
        BorderBrush = UiTheme.Brush(UiTheme.Warning),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 8, 14, 8),
        Margin = new Thickness(0, 8, 0, 0),
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _lockText = new()
    {
        FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.Brush(UiTheme.Text),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _orderStatus = new()
    {
        Foreground = UiTheme.Brush(UiTheme.Success),
        FontSize = UiTheme.FontCaption,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 0, 0)
    };
    private readonly EmptyState _empty;
    private readonly DataGridTemplateColumn _dragColumn = RowDragReorder.HandleColumn();
    private bool _locked;
    private int _formulaId;
    private string _formulaCode = "";
    private List<FormulaItem> _items = new();
    private List<Material> _materials = new();
    private FormulaStepDialog? _stepDialog;
    /// <summary>添加步骤时插入到该步骤之后；null 表示追加到末尾。</summary>
    private int? _addAfterItemId;

    public FormulaEditorPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth,
        IAuditLog audit, NavigationService nav, OrderService orders)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _nav = nav;
        _orders = orders;
        _empty = new EmptyState("该配方还没有称量步骤", "添加步骤", () => EditStep(null));
        Build();
    }

    public void Load(int formulaId)
    {
        _formulaId = formulaId;
        _orderStatus.Text = "";
        Reload();
    }

    public void OnShow()
    {
        if (_formulaId > 0) Reload();
    }

    public void OnBarcode(string code, bool simulated = false)
    {
        if (_stepDialog is null) return;
        var normalized = DeviceHub.NormalizeCode(code);
        if (string.IsNullOrEmpty(normalized)) return;
        _stepDialog.TrySelectMaterial(normalized);
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
        _stepActions.Children.Add(add);
        var up = UiTheme.GhostButton("上移");
        up.Click += (_, _) => Move(-1);
        _stepActions.Children.Add(up);
        var down = UiTheme.GhostButton("下移");
        down.Click += (_, _) => Move(1);
        _stepActions.Children.Add(down);
        var del = UiTheme.DangerButton("删除步骤");
        del.Click += (_, _) => DeleteStep();
        _stepActions.Children.Add(del);
        ToolTipService.SetShowOnDisabled(_stepActions, true);
        bar.Children.Add(_stepActions);
        var finish = UiTheme.PrimaryButton("完成");
        finish.Click += (_, _) => Complete();
        bar.Children.Add(finish);
        bar.Children.Add(_orderStatus);

        header.Children.Add(bar);
        _lockBanner.Child = _lockText;
        header.Children.Add(_lockBanner);
        header.Children.Add(new TextBlock
        {
            Text = "请添加称量步骤并设置大于 0 的上下限（单位 g），可扫码选料。" +
                   "选中某一步后再点「添加步骤」会插入到该步之后；未选中则追加到末尾。" +
                   "按住行首 ⋮⋮ 拖动或用上移 / 下移调整顺序，松手即保存。步骤较多时可滚动浏览（不分页）。",
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
        _grid.Columns.Add(_dragColumn);
        _grid.Columns.Add(GridKit.Text("序号", "Item.Sequence", 60));
        _grid.Columns.Add(GridKit.Text("材料编码", "Item.Material.Code", 140, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("材料名称", "Item.Material.Name", 160, align: TextAlignment.Left));
        _grid.Columns.Add(GridKit.Text("下限(g)", "Item.WeightMin", 90, "0.###"));
        _grid.Columns.Add(GridKit.Text("上限(g)", "Item.WeightMax", 90, "0.###"));
        _grid.Columns.Add(GridKit.Text("备注", "Item.Notes", new DataGridLength(1, DataGridLengthUnitType.Star), align: TextAlignment.Left));
        _grid.MouseDoubleClick += (_, _) =>
        {
            if (!_locked && CurrentStep() is { } it) EditStep(it);
        };
        RowDragReorder.Attach(_grid, () => !_locked, DropStep);

        root.Children.Add(header);
        root.Children.Add(EmptyState.Host(_grid, _empty));
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
        RefreshLock();
        _empty.Update(_items.Count, filtered: false);
        _empty.ActionEnabled = !_locked;

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

    /// <summary>被进行中工单引用的配方禁止修改步骤：显示原因并禁用全部步骤操作。</summary>
    private void RefreshLock()
    {
        var active = _orders.ActiveOrdersForFormula(_formulaId);
        _locked = active.Count > 0;
        _stepActions.IsEnabled = !_locked;
        _dragColumn.Visibility = _locked ? Visibility.Collapsed : Visibility.Visible;
        if (_locked)
        {
            var reason = OrderService.DescribeActiveOrders(active) + "，完成或取消后才能修改步骤。";
            _lockText.Text = "⚠ " + reason;
            _stepActions.ToolTip = reason;
            _lockBanner.Visibility = Visibility.Visible;
        }
        else
        {
            _stepActions.ToolTip = null;
            _lockBanner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>写库前复核：编辑页打开期间可能有人用该配方开了工单。不可改时提示并刷新锁定状态。</summary>
    private bool EnsureEditable()
    {
        try
        {
            _orders.EnsureFormulaStepsEditable(_formulaId);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            UiFeedback.Warn(ex.Message, "无法修改步骤");
            RefreshLock();
            return false;
        }
    }

    private void GoBack()
    {
        if (_items.Count == 0)
        {
            if (!ConfirmDialog.Ask("该配方尚无称量步骤，确定返回列表？", "确认", "确定"))
                return;
        }
        _nav.Navigate(AppPage.Formulas);
    }

    private void EditStep(FormulaItem? existing)
    {
        if (!EnsureEditable()) return;
        if (_materials.Count == 0) { UiFeedback.Warn("请先在「原材料」页创建原材料。"); return; }

        // 新增：记住当前光标行，保存时插到其后；无选中则追加末尾。连续添加时每次后移到新步骤之后。
        _addAfterItemId = existing is null ? CurrentStep()?.Id : null;
        var options = _materials.Select(StepMaterialOption.From).ToList();
        _stepDialog = new FormulaStepDialog(options, existing, DescribeInsertPosition,
            input => SaveStep(existing, input));
        try { _stepDialog.ShowDialog(); }
        finally
        {
            var added = _stepDialog.AddedCount;
            _stepDialog = null;
            _addAfterItemId = null;
            if (added > 0)
                UiFeedback.Success(existing is not null ? "步骤已保存。" : added == 1 ? "步骤已添加。" : $"已添加 {added} 个步骤。");
        }
    }

    private string DescribeInsertPosition()
    {
        if (_addAfterItemId is int id && _items.FirstOrDefault(x => x.Id == id) is { } after)
            return $"将插入到第 {after.Sequence} 步（{after.Material?.Name}）之后。";
        return "将追加到列表末尾。";
    }

    /// <summary>写库；成功返回 null，失败返回错误说明（显示在步骤对话框里）。</summary>
    private string? SaveStep(FormulaItem? existing, StepInput input)
    {
        try
        {
            _orders.EnsureFormulaStepsEditable(_formulaId);
        }
        catch (InvalidOperationException ex)
        {
            RefreshLock();
            return ex.Message;
        }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            int focusId;
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
                    MaterialId = input.MaterialId,
                    Sequence = FormulaSequence.TempOffset + ordered.Count + 1,
                    WeightMin = input.Min,
                    WeightMax = input.Max,
                    Notes = input.Notes
                };
                db.FormulaItems.Add(entity);
                db.SaveChanges();
                focusId = entity.Id;

                var ids = ordered.Select(x => x.Id).ToList();
                ids.Insert(insertIndex, entity.Id);
                ApplySequencePhases(db, ids);
                _addAfterItemId = entity.Id;
            }
            else
            {
                var e = db.FormulaItems.First(i => i.Id == existing.Id);
                e.MaterialId = input.MaterialId;
                e.WeightMin = input.Min;
                e.WeightMax = input.Max;
                e.Notes = input.Notes;
                db.SaveChanges();
                focusId = e.Id;
            }

            _audit.Log("formula", $"编辑配方步骤 formulaId={_formulaId}", _auth.CurrentUser?.Username ?? "");
            Reload(focusId);
            return null;
        }
        catch (Exception ex)
        {
            return $"保存失败：{ex.GetBaseException().Message}";
        }
    }

    private void Move(int dir)
    {
        if (_locked) return;
        if (CurrentStep() is not { } it)
        {
            UiFeedback.Warn("请先选择一个步骤。");
            return;
        }
        var idx = _items.FindIndex(x => x.Id == it.Id);
        var target = idx + dir;
        if (target < 0 || target >= _items.Count) return;

        var ids = _items.Select(x => x.Id).ToList();
        (ids[idx], ids[target]) = (ids[target], ids[idx]);
        SaveOrder(ids, it.Id);
    }

    private void DropStep(object rowObj, int gapIndex)
    {
        if (_locked || rowObj is not SelectableRow<FormulaItem> row) return;
        var ids = _items.Select(x => x.Id).ToList();
        var reordered = FormulaSequence.MoveTo(ids, ids.IndexOf(row.Item.Id), gapIndex);
        if (reordered.SequenceEqual(ids)) return;
        SaveOrder(reordered, row.Item.Id);
    }

    /// <summary>调整顺序后立即写库，不需要单独“保存顺序”。</summary>
    private void SaveOrder(IReadOnlyList<int> ids, int focusId)
    {
        if (!EnsureEditable()) return;
        try
        {
            using var db = _dbFactory.CreateDbContext();
            ApplySequencePhases(db, ids);
        }
        catch (Exception ex)
        {
            UiFeedback.Warn($"调整顺序失败：{ex.GetBaseException().Message}");
            Reload(focusId);
            return;
        }
        Reload(focusId);
        _orderStatus.Text = $"✓ 顺序已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void DeleteStep()
    {
        var selected = GridKit.CheckedItems<FormulaItem>(_grid);
        if (selected.Count == 0 && CurrentStep() is { } one)
            selected = new List<FormulaItem> { one };
        if (selected.Count == 0)
        {
            UiFeedback.Warn("请先勾选或选中要删除的步骤。");
            return;
        }

        if (!EnsureEditable()) return;

        // 已有称重记录（含已完成工单、作废记录）的步骤不能删：删除会让历史记录失去对应步骤
        var used = _orders.StepsWithRecords(selected.Select(x => x.Id));
        if (used.Count > 0)
        {
            var seqs = string.Join("、", selected.Where(x => used.Contains(x.Id))
                .OrderBy(x => x.Sequence).Select(x => $"第 {x.Sequence} 步"));
            UiFeedback.Warn(
                $"{seqs}已有称重记录，不能删除（历史工单需要按步骤追溯）。\n" +
                "如需调整配方，建议复制为新配方后再修改。",
                "无法删除");
            return;
        }

        var tip = selected.Count == 1
            ? "确认删除该步骤？"
            : $"确认删除选中的 {selected.Count} 个步骤？";
        if (!ConfirmDialog.Ask(tip, "确认删除", "删除", danger: true))
            return;

        try
        {
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
        }
        catch (Exception ex)
        {
            UiFeedback.Error($"删除步骤失败：{ex.GetBaseException().Message}", "删除失败");
            Reload();
            return;
        }

        Reload();
        UiFeedback.Success(selected.Count == 1 ? "步骤已删除。" : $"已删除 {selected.Count} 个步骤。");
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

        _audit.Log("formula", $"完成配方编辑 {_formulaCode}", _auth.CurrentUser?.Username ?? "");
        UiFeedback.Success($"配方 {_formulaCode} 已保存，共 {_items.Count} 个步骤。", "配方编辑完成");
        _nav.Navigate(AppPage.Formulas);
    }
}
