using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Workflow;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Export;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using MaterialMgmt.App.Views.Dialogs;

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
    private readonly IConfigService _config;
    private readonly SoundService _sound;
    private readonly ExportService _export;

    private readonly TextBlock _title = new()
    {
        FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiTheme.Brush(UiTheme.Text),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
    };

    private readonly StackPanel _stepList = new() { Margin = new Thickness(0, 0, 0, 8) };

    private readonly StackPanel _scanOkBanner = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 6),
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

    /// <summary>目标范围：扫码后、称量中、超范围时始终可见。</summary>
    private readonly TextBlock _target = new()
    {
        FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush(UiTheme.Primary),
        Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed
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
        FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        Foreground = UiTheme.Brush(UiTheme.Text)
    };

    // 大字读数：放进 Viewbox，窗口缩小 / 高 DPI 时自动缩放，不被裁切
    private readonly TextBlock _weight = new()
    {
        FontSize = 100, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = UiTheme.Brush(UiTheme.WeightUnstable), Text = "— g"
    };
    private readonly Viewbox _weightBox;
    private readonly RangeGauge _gauge = new() { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

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
    private readonly Button _zero = UiTheme.GhostButton("清零");

    // 页内“读数中断”横幅
    private readonly Border _banner = new()
    {
        Background = UiTheme.Brush(UiTheme.DangerSoft),
        BorderBrush = UiTheme.Brush(UiTheme.Danger),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 8, 8, 8),
        Margin = new Thickness(0, 0, 0, 12),
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _bannerText = new()
    {
        FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush(UiTheme.Danger),
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

    private Border _card = null!;
    private DockPanel _sessionView = null!;
    private ScrollViewer _summaryView = null!;

    private WorkflowEngine? _engine;
    private Dictionary<int, double> _validWeights = new();
    private int _workOrderId;
    private int _formulaId;
    private bool _attached;
    private bool _completed;
    private double _liveWeight;
    private bool _liveStable;
    private bool _liveOverload;
    private DateTime _lastWeightAt = DateTime.MinValue;
    private DateTime _attachedAt = DateTime.MinValue;
    private DateTime _zeroedAt = DateTime.MinValue;
    private bool _readingLost;
    private WeighState _lastState = WeighState.WaitingScan;
    private int _outOfRangeCount;
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>秤读数超过该时长未更新即视为过期（ScaleWorker 约 200ms 一帧）；与 250ms 巡检合计 ≤1.5 s 出现横幅。</summary>
    private static readonly TimeSpan ReadingTimeout = TimeSpan.FromSeconds(1.2);

    /// <summary>清零后的保护时间：这段时间内的读数可能仍是清零前的，不允许确认。</summary>
    private static readonly TimeSpan ZeroGuard = TimeSpan.FromSeconds(1);

    /// <summary>读数偏离 0 超过该值时，清零前需要二次确认（防止带着物料误清零）。</summary>
    private const double ZeroConfirmThreshold = 2.0;

    public WeighSessionPanel(
        IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        IAuthService auth,
        IAuditLog audit,
        OrderService orders,
        DeviceHub devices,
        NavigationService nav,
        WeighWorkspace weigh,
        IConfigService config,
        SoundService sound,
        ExportService export)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _audit = audit;
        _orders = orders;
        _devices = devices;
        _nav = nav;
        _weigh = weigh;
        _config = config;
        _sound = sound;
        _export = export;
        _weightBox = new Viewbox
        {
            Child = _weight,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            MinHeight = 70,
            MaxHeight = 150,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Focusable = true;
        _watchdog.Tick += (_, _) => CheckReadingFresh();
        Build();
    }

    public void Load(int workOrderId, int formulaId)
    {
        _workOrderId = workOrderId;
        _formulaId = formulaId;
        _completed = false;
        ShowSessionView();
        StartSession();
    }

    public void OnShow()
    {
        if (_completed)
        {
            Focus();
            return;
        }
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
        if (status.IsError) _sound.Play(SoundCue.WrongScan);
        ApplyStatus(status);
        RefreshUi();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        // 顶栏：左“暂停并返回”（保留进度），右“取消工单”（次要、破坏性操作放远一点）
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        var cancel = UiTheme.GhostButton("取消工单");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Foreground = UiTheme.Brush(UiTheme.Danger);
        cancel.ToolTip = "作废本工单（已称记录保留以备追溯）";
        cancel.Click += (_, _) => Cancel();
        DockPanel.SetDock(cancel, Dock.Right);
        var pause = UiTheme.GhostButton("← 暂停并返回");
        pause.ToolTip = "保留已称进度，可在“我的工单”中恢复称量";
        pause.Click += (_, _) => Pause();
        DockPanel.SetDock(pause, Dock.Left);
        bar.Children.Add(cancel);
        bar.Children.Add(pause);
        bar.Children.Add(_title);

        // 读数中断横幅
        var bannerRow = new DockPanel();
        var reconnect = UiTheme.DangerButton("重连");
        reconnect.Margin = new Thickness(12, 0, 0, 0);
        reconnect.Click += (_, _) =>
        {
            _devices.ReconnectScale(_config);
            _bannerText.Text = "正在重连电子秤…";
            FocusPanel();
        };
        DockPanel.SetDock(reconnect, Dock.Right);
        bannerRow.Children.Add(reconnect);
        bannerRow.Children.Add(_bannerText);
        _banner.Child = bannerRow;
        DockPanel.SetDock(_banner, Dock.Top);

        // —— 称量视图：左侧步骤列表 + 中间主卡片 ——
        _sessionView = new DockPanel();

        var side = new Border
        {
            Width = 260,
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
        sideStack.Children.Add(sideTitle);
        sideStack.Children.Add(new ScrollViewer
        {
            Content = _stepList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        side.Child = sideStack;

        _scanOkBanner.Children.Add(_scanOkMark);
        _scanOkBanner.Children.Add(_scanOkName);

        _card = UiTheme.Card(new StackPanel(), new Thickness(28, 24, 28, 28));
        var center = (StackPanel)_card.Child!;
        center.Children.Add(_progress);
        center.Children.Add(_scanOkBanner);
        center.Children.Add(_target);
        center.Children.Add(_guide);
        center.Children.Add(_weightBox);
        center.Children.Add(_gauge);
        center.Children.Add(_status);

        _zero.Width = 110;
        _zero.Height = 44;
        _zero.Margin = new Thickness(0, 0, 12, 0);
        _zero.Click += (_, _) => Zero();
        ToolTipService.SetShowOnDisabled(_zero, true);
        _confirm.Width = 220;
        _confirm.Height = 44;
        _confirm.Margin = new Thickness(0);
        _confirm.Click += (_, _) => Confirm();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0)
        };
        actions.Children.Add(_zero);
        actions.Children.Add(_confirm);
        center.Children.Add(actions);
        center.Children.Add(_hint);

        _sessionView.Children.Add(side);
        _sessionView.Children.Add(new ScrollViewer
        {
            Content = _card,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        // —— 完工汇总视图 ——
        _summaryView = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed
        };

        var body = new Grid();
        body.Children.Add(_sessionView);
        body.Children.Add(_summaryView);

        root.Children.Add(bar);
        root.Children.Add(_banner);
        root.Children.Add(body);
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

    private void ShowSessionView()
    {
        _sessionView.Visibility = Visibility.Visible;
        _summaryView.Visibility = Visibility.Collapsed;
    }

    private void FocusPanel()
    {
        Focus();
        Keyboard.Focus(this);
    }

    private void OnPanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || _engine is null) return;
        // 称量中空格只用于确认：即使焦点落在“清零/重称”按钮上，也不能被空格误触发
        e.Handled = true;
        if (_engine.State == WeighState.WeightOk) Confirm();
    }

    private void StartSession()
    {
        using var db = _dbFactory.CreateDbContext();
        var wo = db.WorkOrders.Include(w => w.Records)
            .First(w => w.Id == _workOrderId);
        var items = db.FormulaItems.Include(i => i.Material)
            .Where(i => i.FormulaId == _formulaId).ToList();
        _engine = WorkflowEngine.Resume(wo, items);
        _lastState = _engine.State;
        _outOfRangeCount = 0;
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
        _attachedAt = DateTime.Now;
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
        _banner.Visibility = Visibility.Collapsed;
    }

    /// <summary>未连接，或超过 <see cref="ReadingTimeout"/> 没有新读数（刚进入页面时从进入时刻算起）。</summary>
    private bool IsReadingStale()
    {
        if (!_devices.IsScaleConnected) return true;
        var since = _lastWeightAt > _attachedAt ? _lastWeightAt : _attachedAt;
        return DateTime.Now - since > ReadingTimeout;
    }

    /// <summary>
    /// 读数过期（断线 / 无新数据）：显示页内横幅；称量中同时撤销“合格可确认”状态，防止对过期读数确认。
    /// </summary>
    private void CheckReadingFresh()
    {
        var stale = IsReadingStale();
        UpdateBanner(stale);
        UpdateZeroButton(stale);
        if (!stale || _readingLost) return;

        _readingLost = true;
        _liveStable = false;
        _sound.Play(SoundCue.ReadingLost);

        if (_engine is null || _engine.State is not (WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk))
        {
            RenderWeight();
            return;
        }

        var status = _engine.OnReadingLost();
        ApplyStatus(status);
        RefreshUi(keepGuideFromStatus: true, reloadSteps: false);
    }

    private void UpdateBanner(bool stale)
    {
        if (!stale)
        {
            _banner.Visibility = Visibility.Collapsed;
            return;
        }

        if (_devices.IsDiscovering)
            _bannerText.Text = "⚠ 正在识别电子秤…";
        else if (!_devices.IsScaleConnected)
        {
            var err = string.IsNullOrWhiteSpace(_devices.ScaleLastError) ? "" : $"（{_devices.ScaleLastError}）";
            _bannerText.Text = $"⚠ 电子秤未连接{err}。请检查串口线与电源，恢复前无法确认称重。";
        }
        else
            _bannerText.Text = "⚠ 电子秤读数中断。请检查串口线与电源，读数恢复前无法确认称重。";
        _banner.Visibility = Visibility.Visible;
    }

    private void UpdateZeroButton(bool stale)
    {
        var active = _engine is { IsComplete: false };
        var enabled = active && !stale;
        _zero.IsEnabled = enabled;
        _zero.ToolTip = enabled
            ? "把当前读数置为 0（秤盘为空或只放了空容器时使用）"
            : active ? "电子秤未连接或读数中断，无法清零" : null;
    }

    private void OnWeight(double weight, bool stable, bool overload)
    {
        // 来自 ScaleWorker 线程；异步投递避免阻塞读数循环 / Stop()
        Dispatcher.InvokeAsync(() =>
        {
            if (!_attached) return;
            _lastWeightAt = DateTime.Now;
            _readingLost = false;
            UpdateBanner(false);
            UpdateZeroButton(false);
            _liveWeight = weight;
            _liveStable = stable;
            _liveOverload = overload;

            if (_engine is null) { RenderWeight(); return; }
            if (_engine.State == WeighState.WaitingScan || _engine.State == WeighState.Complete)
            {
                RenderWeight();
                return;
            }

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

        var state = _engine?.State;
        var item = _engine?.CurrentItem;
        var active = state is (WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk) && item is not null;

        if (_engine is null)
        {
            _weight.Foreground = UiTheme.Brush(UiTheme.WeightUnstable);
        }
        else
        {
            _weight.Foreground = UiTheme.Brush(state switch
            {
                WeighState.WeightOk => UiTheme.WeightOk,
                WeighState.OutOfRange => UiTheme.WeightBad,
                _ when _liveOverload => UiTheme.WeightBad,
                _ => UiTheme.WeightUnstable
            });
        }

        // 整张卡片的底色随状态变化：合格=浅绿，超范围=浅红（离得远也能看出能不能确认）
        _card.Background = state switch
        {
            WeighState.WeightOk => UiTheme.Brush(UiTheme.SuccessSoft),
            WeighState.OutOfRange => UiTheme.Brush(UiTheme.DangerSoft),
            _ => Brushes.White
        };

        if (active)
        {
            _gauge.Visibility = Visibility.Visible;
            _gauge.Update(item!.WeightMin, item.WeightMax, _readingLost ? null : _liveWeight,
                _liveStable && !_liveOverload);
        }
        else
        {
            _gauge.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyStatus(StepStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            _guide.Text = status.Message;
            _guide.Foreground = UiTheme.Brush(status.IsError ? UiTheme.Danger : UiTheme.Text);
        }
        _confirm.IsEnabled = status.State == WeighState.WeightOk;

        // 提示音只在状态“进入”时响一次，不随 200ms 一帧的读数重复
        if (status.State != _lastState)
        {
            if (status.State == WeighState.WeightOk) _sound.Play(SoundCue.StableOk);
            else if (status.State == WeighState.OutOfRange)
            {
                _outOfRangeCount++;
                _sound.Play(SoundCue.OutOfRange);
            }
            _lastState = status.State;
        }

        if (status.State == WeighState.Complete) Finish();
    }

    private void RefreshUi(bool keepGuideFromStatus = false, bool reloadSteps = true)
    {
        if (_engine is null) return;
        if (reloadSteps)
        {
            _validWeights = _orders.GetValidWeights(_workOrderId);
            RebuildStepList();
        }

        var item = _engine.CurrentItem;
        var weighing = _engine.State is WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk;

        if (weighing && item is not null)
        {
            _scanOkBanner.Visibility = Visibility.Visible;
            _scanOkName.Text = item.Material.Name;
            _target.Text = $"目标范围  {FormatG(item.WeightMin)} ~ {FormatG(item.WeightMax)} g";
            _target.Visibility = Visibility.Visible;
        }
        else
        {
            _scanOkBanner.Visibility = Visibility.Collapsed;
            _scanOkName.Text = "";
            _target.Visibility = Visibility.Collapsed;
        }

        if (_engine.IsComplete)
        {
            _progress.Text = $"已完成 {_engine.TotalCount} / {_engine.TotalCount} 步";
            if (!keepGuideFromStatus) _guide.Text = "全部步骤已完成";
            _status.Text = "";
            _confirm.IsEnabled = false;
            return;
        }

        _progress.Text = $"第 {_engine.StepIndex + 1} / {_engine.TotalCount} 步 · 已完成 {_validWeights.Count} 步";

        if (!keepGuideFromStatus && item is not null && _engine.State == WeighState.WaitingScan)
        {
            _guide.Text =
                $"步骤 {_engine.StepIndex + 1}/{_engine.TotalCount}\n" +
                $"请扫描 【{item.Material.Name}】 的条码（{item.Material.Code}）\n" +
                $"目标：{FormatG(item.WeightMin)} ~ {FormatG(item.WeightMax)} g";
            _guide.Foreground = UiTheme.Brush(UiTheme.Text);
        }

        UpdateStatusLine();
        _confirm.IsEnabled = _engine.State == WeighState.WeightOk;
        _hint.Visibility = weighing ? Visibility.Visible : Visibility.Collapsed;
        UpdateZeroButton(IsReadingStale());
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
            var done = _validWeights.TryGetValue(item.Id, out var actual);
            var current = !_engine.IsComplete && i == _engine.StepIndex;

            var bg = done
                ? UiTheme.Brush(UiTheme.SuccessSoft)
                : UiTheme.Brush(UiTheme.PanelAlt);
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
                Margin = new Thickness(0, 0, 0, 6)
            };

            var lines = new StackPanel();
            lines.Children.Add(new StackPanel
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
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 190
                    }
                }
            });
            lines.Children.Add(new TextBlock
            {
                Text = item.Material.Code,
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.Brush(UiTheme.Muted),
                Margin = new Thickness(24, 2, 0, 0)
            });
            lines.Children.Add(new TextBlock
            {
                Text = $"目标 {FormatG(item.WeightMin)} ~ {FormatG(item.WeightMax)} g",
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.Brush(UiTheme.Muted),
                Margin = new Thickness(24, 2, 0, 0)
            });

            if (done)
            {
                var measured = new DockPanel { Margin = new Thickness(24, 4, 0, 0) };
                var redo = UiTheme.GhostButton("重称");
                redo.Margin = new Thickness(8, 0, 0, 0);
                redo.Padding = new Thickness(10, 1, 10, 1);
                redo.FontSize = UiTheme.FontCaption;
                redo.ToolTip = "作废本步已有记录并重新称量";
                redo.Click += (_, _) => TryReworkStep(idx, item);
                DockPanel.SetDock(redo, Dock.Right);
                measured.Children.Add(redo);
                measured.Children.Add(new TextBlock
                {
                    Text = $"实测 {actual:F3} g",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = UiTheme.Brush(UiTheme.Success),
                    VerticalAlignment = VerticalAlignment.Center
                });
                lines.Children.Add(measured);
            }

            row.Child = lines;
            _stepList.Children.Add(row);
        }
    }

    private void TryReworkStep(int index, FormulaItem item)
    {
        if (_engine is null || _engine.IsComplete) return;
        if (!_validWeights.ContainsKey(item.Id)) return;

        var tip =
            $"重新称量第 {index + 1} 步「{item.Material.Name}」？\n" +
            "将作废该步原有效记录；其后已完成步骤保留。";
        if (!ConfirmDialog.Ask(tip, "重新称量", "重新称量", danger: true))
        {
            FocusPanel();
            return;
        }

        var n = _orders.InvalidateValidRecords(_workOrderId, item.Id, _auth.CurrentUser?.Username);
        _audit.Log("order",
            $"工单#{_workOrderId} 回称步骤 item={item.Id}（作废 {n} 条记录）",
            _auth.CurrentUser?.Username ?? "");
        var status = _engine.RewindToStep(index);
        ApplyStatus(status);
        RefreshUi();
        FocusPanel();
    }

    private void UpdateStatusLine()
    {
        if (_engine is null)
        {
            _status.Text = "";
            return;
        }

        var state = _engine.State;
        var active = state is WeighState.Weighing or WeighState.OutOfRange or WeighState.WeightOk;

        // 称量中只要读数不稳（或超载）就统一显示“稳定中”，不论当前是哪个子状态
        if (active && (!_liveStable || _liveOverload))
        {
            if (_readingLost)
            {
                _status.Text = "读数中断，等待恢复";
                _status.Foreground = UiTheme.Brush(UiTheme.Danger);
            }
            else
            {
                _status.Text = "稳定中";
                _status.Foreground = UiTheme.Brush(UiTheme.Warning);
            }
            return;
        }

        switch (state)
        {
            case WeighState.WaitingScan:
                _status.Text = "等待扫码";
                _status.Foreground = UiTheme.Brush(UiTheme.Muted);
                break;
            case WeighState.Weighing:
                _status.Text = "等待称重，请放置原料";
                _status.Foreground = UiTheme.Brush(UiTheme.Muted);
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
    }

    private void Zero()
    {
        if (_engine is null || IsReadingStale())
        {
            FocusPanel();
            return;
        }

        if (Math.Abs(_liveWeight) > ZeroConfirmThreshold &&
            !ConfirmDialog.Ask(
                $"当前读数为 {_liveWeight:F3} g，清零后这个重量会被当作 0。\n请确认秤盘上没有物料（或只放了空容器）。",
                "确认清零", "清零"))
        {
            FocusPanel();
            return;
        }

        if (!_devices.SendZero())
        {
            UiFeedback.Warn("电子秤未连接，无法清零。");
            FocusPanel();
            return;
        }

        _zeroedAt = DateTime.Now;
        _audit.Log("order", $"工单#{_workOrderId} 清零（清零前读数 {_liveWeight:F3} g）",
            _auth.CurrentUser?.Username ?? "");
        UiFeedback.Success("已发送清零指令");
        FocusPanel();
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

        // 刚清零：当前显示的读数可能还是清零前的，等新读数到达再确认
        if (DateTime.Now - _zeroedAt < ZeroGuard) return;

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
        FocusPanel();
    }

    /// <summary>暂停：不改变工单状态，已确认的步骤都在库里；之后从“我的工单”恢复。</summary>
    private void Pause()
    {
        if (_engine is null || _completed) return;
        _audit.Log("order", $"暂停工单 #{_workOrderId}", _auth.CurrentUser?.Username ?? "");
        LeaveSession();
        UiFeedback.Success("进度已保存，可在“我的工单”中恢复称量");
        _nav.Navigate(AppPage.OperatorHistory);
    }

    private void LeaveSession()
    {
        DetachScale();
        _engine = null;
        _weigh.SetExclusiveBarcode(false);
    }

    private void Finish()
    {
        if (_completed) return;
        _completed = true;

        _orders.CompleteWorkOrder(_workOrderId);
        _audit.Log("order", $"完成工单 #{_workOrderId}", _auth.CurrentUser?.Username ?? "");
        LeaveSession();
        ShowSummary();
    }

    /// <summary>完工后在页内显示汇总（替代原来的模态“工单已完成！”），并给出导出 / 下一步入口。</summary>
    private void ShowSummary()
    {
        OrderSummary summary;
        string formulaName;
        string productionNo;
        try
        {
            summary = _orders.GetSummary(_workOrderId);
            using var db = _dbFactory.CreateDbContext();
            var wo = db.WorkOrders.Include(w => w.Formula).First(w => w.Id == _workOrderId);
            formulaName = wo.Formula.Name;
            productionNo = wo.ProductionNo ?? "";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"工单已完成，但读取汇总失败：{ex.Message}", "提示");
            _nav.Navigate(AppPage.OperatorHistory);
            return;
        }

        var stack = new StackPanel { Margin = new Thickness(0) };
        stack.Children.Add(new TextBlock
        {
            Text = "✓ 工单已完成",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Success),
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"工单 #{_workOrderId} · {formulaName}" + (productionNo.Length > 0 ? $" · 生产编号 {productionNo}" : ""),
            FontSize = 14,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            Margin = new Thickness(0, 0, 0, 20)
        });

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 24) };
        var duration = summary.EndTime.HasValue ? summary.EndTime.Value - summary.StartTime : (TimeSpan?)null;
        grid.Children.Add(Stat("起止时长", duration.HasValue ? FormatDuration(duration.Value) : "—", "含暂停期间"));
        grid.Children.Add(Stat("称量步骤", $"{summary.StepCount} 步", "有效记录"));
        grid.Children.Add(Stat("重称次数", $"{summary.ReworkCount} 次", "作废后重新称量"));
        grid.Children.Add(Stat("本次超范围提示", $"{_outOfRangeCount} 次", "本次打开期间"));
        stack.Children.Add(grid);

        var export = UiTheme.PrimaryButton("导出 Excel");
        export.Height = 40;
        export.Padding = new Thickness(20, 0, 20, 0);
        export.Click += (_, _) => ExportOrder(formulaName);
        var mine = UiTheme.GhostButton("我的工单");
        mine.Height = 40;
        mine.Click += (_, _) => _nav.Navigate(AppPage.OperatorHistory);
        var again = UiTheme.GhostButton("再称一单");
        again.Height = 40;
        again.Click += (_, _) => _nav.Navigate(AppPage.SessionSelect);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { export, again, mine }
        });

        var card = UiTheme.Card(stack, new Thickness(32, 28, 32, 32));
        card.MaxWidth = 640;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        _summaryView.Content = card;
        _sessionView.Visibility = Visibility.Collapsed;
        _summaryView.Visibility = Visibility.Visible;
    }

    private static Border Stat(string label, string value, string note) => new()
    {
        Margin = new Thickness(0, 0, 12, 12),
        Padding = new Thickness(14, 10, 14, 10),
        Background = UiTheme.Brush(UiTheme.PanelAlt),
        CornerRadius = new CornerRadius(8),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = label, FontSize = UiTheme.FontCaption, Foreground = UiTheme.Brush(UiTheme.Muted) },
                new TextBlock
                {
                    Text = value, FontSize = 22, FontWeight = FontWeights.Bold,
                    Foreground = UiTheme.Brush(UiTheme.Text), Margin = new Thickness(0, 2, 0, 0)
                },
                new TextBlock { Text = note, FontSize = 11, Foreground = UiTheme.Brush(UiTheme.Muted) }
            }
        }
    };

    private void ExportOrder(string formulaName)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel|*.xlsx",
            FileName = ExportService.SuggestFilename(_workOrderId, formulaName)
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _export.ExportWorkOrder(_workOrderId, dlg.FileName);
            UiFeedback.Success("已导出");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel()
    {
        if (!ConfirmDialog.Ask("确认取消当前工单？", "确认取消", "取消工单", "返回", danger: true))
        {
            FocusPanel();
            return;
        }
        try
        {
            _orders.Cancel(_workOrderId);
            _audit.Log("order", $"取消工单 #{_workOrderId}", _auth.CurrentUser?.Username ?? "");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        UiFeedback.Success("工单已取消。");
        LeaveSession();
        _nav.Navigate(AppPage.OperatorHistory);
    }

    private static string FormatG(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} 小时 {t.Minutes} 分";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} 分 {t.Seconds} 秒";
        return $"{t.Seconds} 秒";
    }
}
