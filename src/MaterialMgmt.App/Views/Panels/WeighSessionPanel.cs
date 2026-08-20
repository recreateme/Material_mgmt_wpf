using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Workflow;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class WeighSessionPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly OrderService _orders;
    private readonly DeviceHub _devices;
    private readonly NavigationService _nav;

    private readonly TextBlock _title = new()
    {
        FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiTheme.Brush(UiTheme.Text),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
    };

    /// <summary>扫码成功后左上角：绿色勾 + 原材料名。</summary>
    private readonly StackPanel _scanOkBanner = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 16),
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _scanOkMark = new()
    {
        Text = "✓", FontSize = 28, FontWeight = FontWeights.Bold,
        Foreground = UiTheme.Brush(UiTheme.Success), VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0)
    };
    private readonly TextBlock _scanOkName = new()
    {
        FontSize = 22, FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.Brush(UiTheme.Text), VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _guide = new()
    {
        FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        Foreground = UiTheme.Brush(UiTheme.Text)
    };

    private readonly TextBlock _weight = new()
    {
        FontSize = 56, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = UiTheme.Brush(UiTheme.WeightUnstable), Text = "— g"
    };

    private readonly TextBlock _status = new()
    {
        FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 0), Foreground = UiTheme.Brush(UiTheme.Muted)
    };

    private readonly TextBlock _hint = new()
    {
        FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
        Foreground = UiTheme.Brush(UiTheme.Muted), Text = "稳定且合格后，可点击确认或按空格键进入下一材料"
    };

    private readonly Button _confirm = UiTheme.PrimaryButton("确认称重");
    private WorkflowEngine? _engine;
    private int _workOrderId;
    private int _formulaId;
    private bool _attached;
    private double _liveWeight;
    private bool _liveStable;
    private bool _liveOverload;

    public WeighSessionPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth,
        IAuditLog audit,
        OrderService orders,
        DeviceHub devices,
        NavigationService nav)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _orders = orders;
        _devices = devices;
        _nav = nav;
        Focusable = true;
        Build();
    }

    public void Load(int workOrderId, int formulaId)
    {
        _workOrderId = workOrderId;
        _formulaId = formulaId;
        StartSession();
    }

    public void OnShow()
    {
        AttachScale();
        if (_engine is null && _workOrderId > 0) StartSession();
        RefreshUi();
        Focus();
        Keyboard.Focus(this);
    }

    public void OnBarcode(string code, bool simulated = false)
    {
        if (_engine is null) return;
        var status = _engine.OnBarcode(code);
        ApplyStatus(status);
        RefreshUi();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(28) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(bar, Dock.Top);
        var back = UiTheme.GhostButton("取消工单");
        back.Click += (_, _) => Cancel();
        bar.Children.Add(back);
        bar.Children.Add(_title);

        _scanOkBanner.Children.Add(_scanOkMark);
        _scanOkBanner.Children.Add(_scanOkName);

        var card = UiTheme.Card(new StackPanel(), new Thickness(28, 24, 28, 28));
        var center = (StackPanel)card.Child!;
        center.Children.Add(_scanOkBanner);
        center.Children.Add(_guide);
        center.Children.Add(_weight);
        center.Children.Add(_status);
        _confirm.Width = 220;
        _confirm.Height = 44;
        _confirm.Margin = new Thickness(0, 20, 0, 0);
        _confirm.HorizontalAlignment = HorizontalAlignment.Center;
        _confirm.Click += (_, _) => Confirm();
        center.Children.Add(_confirm);
        center.Children.Add(_hint);

        root.Children.Add(bar);
        root.Children.Add(card);
        Content = root;

        PreviewKeyDown += OnPanelKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) DetachScale();
            else
            {
                Focus();
                Keyboard.Focus(this);
            }
        };
    }

    private void OnPanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (_engine?.State != WeighState.WeightOk) return;
        e.Handled = true;
        Confirm();
    }

    private void StartSession()
    {
        using var db = _dbFactory.CreateDbContext();
        var wo = db.WorkOrders.Include(w => w.Records)
            .First(w => w.Id == _workOrderId);
        var items = db.FormulaItems.Include(i => i.Material)
            .Where(i => i.FormulaId == _formulaId).ToList();
        _engine = WorkflowEngine.Resume(wo, items);
        _title.Text = $"称量作业 · 工单 #{_workOrderId}";
        RefreshUi();
        if (_engine.IsComplete) Finish();
    }

    private void AttachScale()
    {
        if (_attached) return;
        var worker = _devices.GetScaleWorker();
        if (worker is null) return;
        worker.WeightUpdated += OnWeight;
        _attached = true;
    }

    private void DetachScale()
    {
        if (!_attached) return;
        var worker = _devices.GetScaleWorker();
        if (worker is not null) worker.WeightUpdated -= OnWeight;
        _attached = false;
    }

    private void OnWeight(double weight, bool stable, bool overload)
    {
        Dispatcher.Invoke(() =>
        {
            _liveWeight = weight;
            _liveStable = stable;
            _liveOverload = overload;
            RenderWeight();

            if (_engine is null) return;
            // 待扫码阶段不跑判定，只刷新读数
            if (_engine.State == WeighState.WaitingScan || _engine.State == WeighState.Complete)
                return;

            var status = _engine.OnWeightUpdate(weight, stable && !overload);
            ApplyStatus(status);
            RefreshUi(keepGuideFromStatus: true);
        });
    }

    private void RenderWeight()
    {
        if (_liveOverload)
            _weight.Text = $"{_liveWeight:F3} g 超载";
        else
            _weight.Text = $"{_liveWeight:F3} g";

        if (_engine is null)
        {
            _weight.Foreground = UiTheme.Brush(UiTheme.WeightUnstable);
            return;
        }

        _weight.Foreground = UiTheme.Brush(_engine.State switch
        {
            WeighState.WeightOk => UiTheme.WeightOk,
            WeighState.OutOfRange => UiTheme.WeightBad,
            _ when _liveOverload => UiTheme.WeightBad,
            _ => UiTheme.WeightUnstable
        });
    }

    private void ApplyStatus(StepStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            _guide.Text = status.Message;
            _guide.Foreground = UiTheme.Brush(status.IsError ? UiTheme.Danger : UiTheme.Text);
        }
        _confirm.IsEnabled = status.State == WeighState.WeightOk;
        if (status.State == WeighState.Complete) Finish();
    }

    private void RefreshUi(bool keepGuideFromStatus = false)
    {
        if (_engine is null) return;
        var item = _engine.CurrentItem;
        var weighing = _engine.State is WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk;

        if (weighing && item is not null)
        {
            _scanOkBanner.Visibility = Visibility.Visible;
            _scanOkName.Text = item.Material.Name;
        }
        else
        {
            _scanOkBanner.Visibility = Visibility.Collapsed;
            _scanOkName.Text = "";
        }

        if (_engine.IsComplete)
        {
            if (!keepGuideFromStatus) _guide.Text = "全部步骤已完成";
            _status.Text = "";
            _confirm.IsEnabled = false;
            return;
        }

        if (!keepGuideFromStatus && item is not null && _engine.State == WeighState.WaitingScan)
        {
            _guide.Text =
                $"步骤 {_engine.StepIndex + 1}/{_engine.TotalCount}\n" +
                $"请扫描 【{item.Material.Name}】 的条码（{item.Material.Code}）\n" +
                $"目标：{item.WeightMin} ~ {item.WeightMax} g";
            _guide.Foreground = UiTheme.Brush(UiTheme.Text);
        }

        UpdateStatusLine();
        _confirm.IsEnabled = _engine.State == WeighState.WeightOk;
        _hint.Visibility = weighing ? Visibility.Visible : Visibility.Collapsed;
        RenderWeight();
    }

    private void UpdateStatusLine()
    {
        if (_engine is null)
        {
            _status.Text = "";
            return;
        }

        switch (_engine.State)
        {
            case WeighState.WaitingScan:
                _status.Text = "等待扫码";
                _status.Foreground = UiTheme.Brush(UiTheme.Muted);
                break;
            case WeighState.Weighing:
                if (!_liveStable || _liveOverload)
                {
                    _status.Text = "稳定中";
                    _status.Foreground = UiTheme.Brush(UiTheme.Warning);
                }
                else
                {
                    _status.Text = "等待称重，请放置原料";
                    _status.Foreground = UiTheme.Brush(UiTheme.Muted);
                }
                break;
            case WeighState.WeightOk:
                _status.Text = "✓ 范围内，可确认（空格键）";
                _status.Foreground = UiTheme.Brush(UiTheme.Success);
                break;
            case WeighState.OutOfRange:
                _status.Text = "✗ 超出目标范围，请调整后等待稳定";
                _status.Foreground = UiTheme.Brush(UiTheme.Danger);
                break;
            default:
                _status.Text = "";
                break;
        }

        // 称重过程中未稳定时，状态行强制显示「稳定中」（覆盖引擎文案侧状态）
        if (_engine.State is WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk)
        {
            if (!_liveStable || _liveOverload)
            {
                _status.Text = "稳定中";
                _status.Foreground = UiTheme.Brush(UiTheme.Warning);
            }
        }
    }

    private void Confirm()
    {
        if (_engine is null || _engine.State != WeighState.WeightOk) return;
        var item = _engine.CurrentItem;
        if (item is null) return;
        var exists = _orders.HasValidRecord(_workOrderId, item.Id);
        var (status, pending) = _engine.ConfirmWeight(exists);
        if (pending is not null)
        {
            _orders.SavePendingWeight(pending.WorkOrderId, pending.FormulaItemId,
                pending.BarcodeScanned, pending.ActualWeight, pending.Timestamp);
            _audit.Log("order", $"工单#{_workOrderId} 确认步骤 item={pending.FormulaItemId} w={pending.ActualWeight:F3}",
                _auth.CurrentUser?.Username ?? "");
        }
        ApplyStatus(status);
        RefreshUi();
        Focus();
        Keyboard.Focus(this);
    }

    private void Finish()
    {
        _orders.CompleteWorkOrder(_workOrderId);
        _audit.Log("order", $"完成工单 #{_workOrderId}", _auth.CurrentUser?.Username ?? "");
        MessageBox.Show("工单已完成！", "完成");
        DetachScale();
        _nav.Navigate(AppPage.OperatorHistory);
    }

    private void Cancel()
    {
        if (MessageBox.Show("确认取消当前工单？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            _orders.Cancel(_workOrderId);
            _audit.Log("order", $"取消工单 #{_workOrderId}", _auth.CurrentUser?.Username ?? "");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        DetachScale();
        _engine = null;
        _nav.Navigate(AppPage.SessionSelect);
    }
}
