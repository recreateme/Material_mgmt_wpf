using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
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
    private readonly WeighWorkspace _weigh;

    private readonly TextBlock _title = new()
    {
        FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiTheme.Brush(UiTheme.Text),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
    };

    private readonly StackPanel _stepList = new() { Margin = new Thickness(0, 0, 0, 8) };

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

    private readonly TextBlock _progress = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.Brush(UiTheme.Primary),
        Margin = new Thickness(0, 0, 0, 12)
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
    private HashSet<int> _validItemIds = new();
    private int _workOrderId;
    private int _formulaId;
    private bool _attached;
    private double _liveWeight;
    private bool _liveStable;
    private bool _liveOverload;
    private DateTime _lastWeightAt = DateTime.MinValue;
    private bool _readingLost;
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>秤读数超过该时长未更新即视为过期（ScaleWorker 约 200ms 一帧）。</summary>
    private static readonly TimeSpan ReadingTimeout = TimeSpan.FromSeconds(1.5);

    public WeighSessionPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth,
        IAuditLog audit,
        OrderService orders,
        DeviceHub devices,
        NavigationService nav,
        WeighWorkspace weigh)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _orders = orders;
        _devices = devices;
        _nav = nav;
        _weigh = weigh;
        Focusable = true;
        _watchdog.Tick += (_, _) => CheckReadingFresh();
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
        if (simulated)
        {
            // 称量流程只接受真实扫码，模拟扫码不得产生称重记录
            ApplyStatus(new StepStatus(_engine.State, "称量中不接受模拟扫码，请使用扫码枪扫描原料条码", IsError: true));
            return;
        }
        var status = _engine.OnBarcode(code);
        ApplyStatus(status);
        RefreshUi();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        var back = UiTheme.GhostButton("取消工单");
        back.Click += (_, _) => Cancel();
        bar.Children.Add(back);
        bar.Children.Add(_title);

        var side = new Border
        {
            Width = 240,
            Margin = new Thickness(0, 0, 16, 0),
            Background = Brushes.White,
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12)
        };
        DockPanel.SetDock(side, Dock.Left);
        var sideStack = new DockPanel();
        var sideTitle = new TextBlock
        {
            Text = "称量进度",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(sideTitle, Dock.Top);
        var sideHint = new TextBlock
        {
            Text = "绿色=已完成（可点回称）\n灰色=待称量",
            FontSize = 11,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(sideHint, Dock.Bottom);
        sideStack.Children.Add(sideTitle);
        sideStack.Children.Add(sideHint);
        sideStack.Children.Add(new ScrollViewer
        {
            Content = _stepList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        side.Child = sideStack;

        _scanOkBanner.Children.Add(_scanOkMark);
        _scanOkBanner.Children.Add(_scanOkName);

        var card = UiTheme.Card(new StackPanel(), new Thickness(28, 24, 28, 28));
        var center = (StackPanel)card.Child!;
        center.Children.Add(_progress);
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
        root.Children.Add(side);
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
        _weigh.SetExclusiveBarcode(true);
        RefreshUi();
        if (_engine.IsComplete) Finish();
    }

    private void AttachScale()
    {
        if (_attached) return;
        // 订阅 DeviceHub 的稳定事件：天平重连、重建 ScaleWorker 后无需重新订阅
        _devices.WeightUpdated += OnWeight;
        _attached = true;
        _lastWeightAt = DateTime.MinValue;
        _readingLost = false;
        _watchdog.Start();
    }

    private void DetachScale()
    {
        if (!_attached) return;
        _devices.WeightUpdated -= OnWeight;
        _watchdog.Stop();
        _attached = false;
    }

    private bool IsReadingStale()
        => !_devices.IsScaleConnected || DateTime.Now - _lastWeightAt > ReadingTimeout;

    /// <summary>读数过期（断线 / 无新数据）时撤销“合格可确认”状态并给出明确提示。</summary>
    private void CheckReadingFresh()
    {
        if (_engine is null) return;
        if (_engine.State is not (WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk)) return;
        if (_readingLost || !IsReadingStale()) return;

        _readingLost = true;
        var status = _engine.OnReadingLost();
        _liveStable = false;
        ApplyStatus(status);
        RefreshUi(keepGuideFromStatus: true, reloadSteps: false);
    }

    private void OnWeight(double weight, bool stable, bool overload)
    {
        // 来自 ScaleWorker 线程；异步投递避免阻塞读数循环 / Stop()
        Dispatcher.InvokeAsync(() =>
        {
            if (!_attached) return;
            _lastWeightAt = DateTime.Now;
            _readingLost = false;
            _liveWeight = weight;
            _liveStable = stable;
            _liveOverload = overload;
            RenderWeight();

            if (_engine is null) return;
            if (_engine.State == WeighState.WaitingScan || _engine.State == WeighState.Complete)
                return;

            var status = _engine.OnWeightUpdate(weight, stable && !overload);
            ApplyStatus(status);
            // 读数每 200ms 一帧，仅刷新读数/状态，不重查数据库、不重建步骤列表
            RefreshUi(keepGuideFromStatus: true, reloadSteps: false);
        });
    }

    private void RenderWeight()
    {
        if (_readingLost)
            _weight.Text = "— g";
        else if (_liveOverload)
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

    private void RefreshUi(bool keepGuideFromStatus = false, bool reloadSteps = true)
    {
        if (_engine is null) return;
        if (reloadSteps)
        {
            _validItemIds = _orders.GetValidFormulaItemIds(_workOrderId);
            RebuildStepList();
        }

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
            _progress.Text = $"已完成 {_engine.TotalCount} / {_engine.TotalCount} 步";
            if (!keepGuideFromStatus) _guide.Text = "全部步骤已完成";
            _status.Text = "";
            _confirm.IsEnabled = false;
            return;
        }

        _progress.Text = $"第 {_engine.StepIndex + 1} / {_engine.TotalCount} 步 · 已完成 {_validItemIds.Count} 步";

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

    private void RebuildStepList()
    {
        _stepList.Children.Clear();
        if (_engine is null) return;

        for (var i = 0; i < _engine.Items.Count; i++)
        {
            var idx = i;
            var item = _engine.Items[i];
            var done = _validItemIds.Contains(item.Id);
            var current = !_engine.IsComplete && i == _engine.StepIndex;

            var bg = done
                ? new SolidColorBrush(Color.FromRgb(0xEC, 0xFC, 0xF5))
                : new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            var borderColor = current ? UiTheme.Primary : (done ? UiTheme.Success : UiTheme.Border);
            var mark = done ? "✓" : (current ? "●" : "○");
            var markColor = done ? UiTheme.Success : (current ? UiTheme.Primary : UiTheme.Muted);

            var row = new Border
            {
                Background = bg,
                BorderBrush = UiTheme.Brush(borderColor),
                BorderThickness = new Thickness(current ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = done ? Cursors.Hand : Cursors.Arrow
            };
            row.Child = new StackPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = mark,
                                Foreground = UiTheme.Brush(markColor),
                                FontWeight = FontWeights.Bold,
                                Margin = new Thickness(0, 0, 8, 0),
                                Width = 16
                            },
                            new TextBlock
                            {
                                Text = $"{i + 1}. {item.Material.Name}",
                                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                                Foreground = UiTheme.Brush(done || current ? UiTheme.Text : UiTheme.Muted),
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = item.Material.Code,
                        FontSize = 11,
                        Foreground = UiTheme.Brush(UiTheme.Muted),
                        Margin = new Thickness(24, 2, 0, 0)
                    }
                }
            };
            if (done)
                row.MouseLeftButtonUp += (_, _) => TryReworkStep(idx, item);
            _stepList.Children.Add(row);
        }
    }

    private void TryReworkStep(int index, FormulaItem item)
    {
        if (_engine is null || _engine.IsComplete) return;
        if (!_validItemIds.Contains(item.Id))
        {
            MessageBox.Show("该步骤尚未完成，无法回称。", "提示");
            return;
        }

        var tip =
            $"重新称量第 {index + 1} 步「{item.Material.Name}」？\n" +
            "将作废该步原有效记录；其后已完成步骤保留。";
        if (MessageBox.Show(tip, "重新称量", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var n = _orders.InvalidateValidRecords(_workOrderId, item.Id, _auth.CurrentUser?.Username);
        _audit.Log("order",
            $"工单#{_workOrderId} 回称步骤 item={item.Id}（作废 {n} 条记录）",
            _auth.CurrentUser?.Username ?? "");
        var status = _engine.RewindToStep(index);
        ApplyStatus(status);
        RefreshUi();
        Focus();
        Keyboard.Focus(this);
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

        // 点击瞬间再校验一次读数新鲜度，避免对断线前的旧读数确认
        if (IsReadingStale())
        {
            CheckReadingFresh();
            return;
        }

        StepStatus status;
        try
        {
            var exists = _orders.HasValidRecord(_workOrderId, item.Id);
            var (prepared, pending) = _engine.PrepareConfirm(exists);
            if (pending is null)
            {
                ApplyStatus(prepared);
                return;
            }

            // 先落库，成功后才前进；失败则保持当前步骤，可直接重试
            _orders.SavePendingWeight(pending.WorkOrderId, pending.FormulaItemId,
                pending.BarcodeScanned, pending.ActualWeight, pending.Timestamp);
            status = _engine.CommitConfirm();

            try
            {
                _audit.Log("order", $"工单#{_workOrderId} 确认步骤 item={pending.FormulaItemId} w={pending.ActualWeight:F3}",
                    _auth.CurrentUser?.Username ?? "");
            }
            catch { /* 审计失败不应影响已落库的称重记录 */ }

            // 记录已落库、状态机已前进；跳过后续已有有效记录的步骤失败时不回滚，保留当前进度
            if (status.State != WeighState.Complete)
            {
                try { status = _engine.SkipWhileCompleted(id => _orders.HasValidRecord(_workOrderId, id)); }
                catch { /* 下次 RefreshUi 会按数据库状态呈现 */ }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存称重记录失败，当前步骤未前进，请重试。\n\n{ex.Message}", "保存失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
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
        _engine = null;
        _weigh.SetExclusiveBarcode(false);
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
        UiFeedback.Success("工单已取消。");
        DetachScale();
        _engine = null;
        _weigh.SetExclusiveBarcode(false);
        _nav.Navigate(AppPage.OperatorHistory);
    }
}
