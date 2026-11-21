using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

/// <summary>
/// 首页。业务人员：开始称量、全部进行中工单（本人的可一键继续）、今日统计；
/// 系统管理员：账号概况、设备状态、数据安全（备份与重复记录修复）。
/// </summary>
public sealed class HomePanel : UserControl, IPageLifecycle
{
    private readonly IAuthService _auth;
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly NavigationService _nav;
    private readonly WeighWorkspace _weigh;
    private readonly DeviceHub _devices;
    private readonly IConfigService _config;
    private readonly DbBootstrap _bootstrap;
    private readonly OrderService _orders;
    private readonly IAuditLog _audit;
    private readonly StackPanel _body = new() { Margin = new Thickness(24) };
    private WrapPanel _deviceRow = new();

    public HomePanel(IAuthService auth, IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        NavigationService nav, WeighWorkspace weigh, DeviceHub devices, IConfigService config,
        DbBootstrap bootstrap, OrderService orders, IAuditLog audit)
    {
        _auth = auth;
        _dbFactory = dbFactory;
        _nav = nav;
        _weigh = weigh;
        _devices = devices;
        _config = config;
        _bootstrap = bootstrap;
        _orders = orders;
        _audit = audit;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) =>
        {
            _devices.ScaleStatusChanged += OnDeviceChanged;
            _devices.ScannerStatusChanged += OnDeviceChanged;
        };
        Unloaded += (_, _) =>
        {
            _devices.ScaleStatusChanged -= OnDeviceChanged;
            _devices.ScannerStatusChanged -= OnDeviceChanged;
        };
    }

    public void OnShow() => Rebuild();

    private void OnDeviceChanged() => Dispatcher.BeginInvoke(RefreshDevices);

    private void Rebuild()
    {
        _body.Children.Clear();
        var user = _auth.CurrentUser;
        if (user is null) return;

        _body.Children.Add(Banner(user));

        if (user.IsBusiness())
        {
            AddActiveOrders(user);
            AddTodayStats(user);
            AddShortcuts(new (string, string, Action)[]
            {
                ("原材料", "维护原材料主数据", () => _nav.Navigate(AppPage.Materials)),
                ("配方管理", "维护配方与称量步骤", () => _nav.Navigate(AppPage.Formulas)),
                ("工单历史", "查看与导出全部工单", () => _nav.Navigate(AppPage.AdminHistory)),
                ("我的工单", "在称量窗中查看与恢复", () => _weigh.Open(AppPage.OperatorHistory)),
                ("系统设置", "串口、扫码与提示音", () => _nav.Navigate(AppPage.Settings)),
            });
            AddDevices();
        }
        else
        {
            AddAccountOverview();
            AddDevices();
            if (user.IsSystemAdmin()) AddDataSafetySection();
            AddShortcuts(new (string, string, Action)[]
            {
                ("用户管理", "维护业务人员账号", () => _nav.Navigate(AppPage.UserMgmt)),
                ("系统设置", "串口、扫码与提示音", () => _nav.Navigate(AppPage.Settings)),
            });
        }
    }

    private Border Banner(CurrentUser user)
    {
        var text = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = $"{Greeting()}，{user.DisplayName}",
                    FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = $"今天是 {DateTime.Now:yyyy年M月d日 dddd}",
                    FontSize = 14,
                    Foreground = UiTheme.Brush(UiTheme.WithAlpha(Colors.White, 0xE6)),
                    Margin = new Thickness(0, 6, 0, 0)
                },
                new TextBlock
                {
                    Text = $"{RoleLabels.ToDisplay(user.Role)}  ·  原料管理系统 WPF {AppInfo.DisplayVersion}",
                    FontSize = 12,
                    Foreground = UiTheme.Brush(UiTheme.WithAlpha(Colors.White, 0xB3)),
                    Margin = new Thickness(0, 4, 0, 0)
                }
            }
        };

        var row = new DockPanel();
        if (user.IsBusiness())
        {
            var start = new Button
            {
                Content = "▶  开始称量",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(28, 12, 28, 12),
                Background = Brushes.White,
                Foreground = UiTheme.Brush(UiTheme.Primary),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "打开称量窗，选择配方开始新工单"
            };
            start.Click += (_, _) => _weigh.Open(AppPage.SessionSelect);
            DockPanel.SetDock(start, Dock.Right);
            row.Children.Add(start);
        }
        row.Children.Add(text);

        return new Border
        {
            Background = new LinearGradientBrush(UiTheme.BannerStart, UiTheme.BannerEnd, 0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(28, 24, 28, 24),
            Margin = new Thickness(0, 0, 0, 18),
            Child = row
        };
    }

    /// <summary>全部进行中工单：本人的可直接继续；他人的只显示，避免替别人称量。</summary>
    private void AddActiveOrders(CurrentUser user)
    {
        var active = _orders.ActiveOrders();
        _body.Children.Add(SectionTitle(active.Count == 0 ? "进行中的工单" : $"进行中的工单（{active.Count}）"));
        if (active.Count == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = "当前没有进行中的工单。",
                Foreground = UiTheme.Brush(UiTheme.Muted),
                Margin = new Thickness(0, 0, 0, 18)
            });
            return;
        }

        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 18), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var o in active)
        {
            var mine = o.OperatorId == user.Id;
            var action = mine ? UiTheme.PrimaryButton("继续称量") : UiTheme.GhostButton("继续称量");
            action.Margin = new Thickness(12, 0, 0, 0);
            action.VerticalAlignment = VerticalAlignment.Center;
            if (mine)
            {
                var (id, formulaId) = (o.Id, o.FormulaId);
                action.Click += (_, _) => _weigh.OpenResume(id, formulaId);
            }
            else
            {
                action.IsEnabled = false;
                ToolTipService.SetShowOnDisabled(action, true);
                action.ToolTip = $"由 {o.Operator} 进行中，只能由本人继续";
            }

            var row = new DockPanel();
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"#{o.Id}  {o.FormulaCode} {o.FormulaName}",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = UiTheme.Brush(UiTheme.Text)
                    },
                    new TextBlock
                    {
                        Text = DescribeOrder(o, mine),
                        FontSize = UiTheme.FontCaption,
                        Foreground = UiTheme.Brush(UiTheme.Muted),
                        Margin = new Thickness(0, 4, 0, 0)
                    }
                }
            });

            var card = UiTheme.Card(row, new Thickness(16, 10, 12, 10));
            card.Margin = new Thickness(0, 0, 0, 8);
            card.BorderBrush = UiTheme.Brush(mine ? UiTheme.Warning : UiTheme.Border);
            list.Children.Add(card);
        }
        _body.Children.Add(list);
    }

    private static string DescribeOrder(ActiveOrderInfo o, bool mine)
    {
        var parts = new List<string>
        {
            mine ? "本人" : o.Operator,
            $"开始于 {o.StartTime:MM-dd HH:mm}",
            $"已完成 {o.DoneSteps} / {o.TotalSteps} 步"
        };
        if (!string.IsNullOrWhiteSpace(o.ProductionNo)) parts.Insert(1, $"生产编号 {o.ProductionNo}");
        return string.Join("  ·  ", parts);
    }

    private void AddTodayStats(CurrentUser user)
    {
        var all = TodaySummary.For(_orders.OrdersStartedOn(DateTime.Today), DateTime.Today);
        var mine = TodaySummary.For(_orders.OrdersStartedOn(DateTime.Today, user.Id), DateTime.Today);
        _body.Children.Add(SectionTitle("今日"));
        var stats = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        stats.Children.Add(Stat("今日工单", all.Started.ToString(), $"其中本人 {mine.Started}"));
        stats.Children.Add(Stat("今日完成", all.Completed.ToString(), $"其中本人 {mine.Completed}"));
        stats.Children.Add(Stat("含重称工单", all.WithRework.ToString(), "有作废记录的工单"));
        _body.Children.Add(stats);
    }

    private void AddAccountOverview()
    {
        _body.Children.Add(SectionTitle("账号概况"));
        var stats = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        using (var db = _dbFactory.CreateDbContext())
        {
            var total = db.Users.Count(u => u.Role == Roles.Business);
            var active = db.Users.Count(u => u.Role == Roles.Business && u.IsActive);
            stats.Children.Add(Stat("业务人员", total.ToString()));
            stats.Children.Add(Stat("启用", active.ToString()));
            stats.Children.Add(Stat("停用", (total - active).ToString()));
        }
        _body.Children.Add(stats);
    }

    private void AddShortcuts(IEnumerable<(string Title, string Desc, Action Go)> items)
    {
        _body.Children.Add(SectionTitle("快捷入口"));
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        foreach (var (title, desc, go) in items)
            cards.Children.Add(ShortcutCard(title, desc, go));
        _body.Children.Add(cards);
    }

    private void AddDevices()
    {
        _body.Children.Add(SectionTitle("设备状态"));
        _deviceRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        _body.Children.Add(_deviceRow);
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        if (_deviceRow.Parent is null) return;
        _deviceRow.Children.Clear();
        _deviceRow.Children.Add(DeviceCard("电子天平", DeviceStatusText.Scale(_devices), () => _devices.ReconnectScale(_config)));
        _deviceRow.Children.Add(DeviceCard("扫码枪", DeviceStatusText.Scanner(_devices), () => _devices.ApplyScannerConfig(_config)));
    }

    /// <summary>系统管理员：最近备份时间、立即备份；称重记录唯一索引未建成时给出修复入口。</summary>
    private void AddDataSafetySection()
    {
        _body.Children.Add(SectionTitle("数据安全"));

        if (!_bootstrap.ValidIndexReady)
        {
            var dupes = _orders.CountDuplicateValidGroups();
            var warn = new DockPanel();
            var fix = UiTheme.DangerButton("修复");
            fix.Margin = new Thickness(12, 0, 0, 0);
            fix.Click += async (_, _) => await RepairDuplicatesAsync(dupes);
            DockPanel.SetDock(fix, Dock.Right);
            warn.Children.Add(fix);
            warn.Children.Add(new TextBlock
            {
                Text = $"⚠ 发现 {dupes} 组“同一工单同一步骤有多条有效称重记录”的历史数据，数据库保护未生效。\n" +
                       "修复将保留每组最新一条，其余标为作废（原因：系统修复），可在工单详情中查看。修复前会先自动备份。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiTheme.Brush(UiTheme.Danger),
                VerticalAlignment = VerticalAlignment.Center
            });
            var warnCard = UiTheme.Card(warn, new Thickness(16, 12, 12, 12));
            warnCard.Background = UiTheme.Brush(UiTheme.DangerSoft);
            warnCard.BorderBrush = UiTheme.Brush(UiTheme.Danger);
            warnCard.Margin = new Thickness(0, 0, 0, 12);
            _body.Children.Add(warnCard);
        }

        var latest = _bootstrap.LatestBackupTime();
        var text = new TextBlock
        {
            Text = latest is null ? "尚无备份" : $"最近备份：{latest:yyyy-MM-dd HH:mm}",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var hint = new TextBlock
        {
            Text = $"每次启动与退出自动备份，各保留最近 14 份。位置：{_bootstrap.BackupDir}",
            FontSize = UiTheme.FontCaption,
            Foreground = UiTheme.Brush(UiTheme.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var now = UiTheme.GhostButton("立即备份");
        now.Margin = new Thickness(12, 0, 0, 0);
        now.Click += async (_, _) =>
        {
            now.IsEnabled = false;
            var path = await _bootstrap.BackupAsync("manual");
            now.IsEnabled = true;
            if (path is null)
            {
                UiFeedback.Warn("备份失败，详情见日志。");
                return;
            }
            _audit.Log("system", $"手动备份数据库 {Path.GetFileName(path)}", _auth.CurrentUser?.Username ?? "");
            text.Text = $"最近备份：{DateTime.Now:yyyy-MM-dd HH:mm}";
            UiFeedback.Success("数据库已备份");
        };
        var row = new DockPanel();
        DockPanel.SetDock(now, Dock.Right);
        row.Children.Add(now);
        row.Children.Add(new StackPanel { Children = { text, hint } });
        var card = UiTheme.Card(row, new Thickness(16, 12, 12, 12));
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(0, 0, 0, 18);
        _body.Children.Add(card);
    }

    private async Task RepairDuplicatesAsync(int groups)
    {
        if (!ConfirmDialog.Ask(
                $"将修复 {groups} 组重复有效记录：每组保留最新一条，其余标为作废。\n修复前会先备份数据库。是否继续？",
                "修复称重记录", "开始修复", danger: true))
            return;
        try
        {
            await _bootstrap.BackupAsync("repair");
            var n = _orders.RepairDuplicateValidRecords(_auth.CurrentUser?.Username);
            var ok = await _bootstrap.TryCreateValidIndexAsync();
            _audit.Log("system", $"修复重复有效称重记录：作废 {n} 条，索引{(ok ? "已建立" : "仍未建立")}",
                _auth.CurrentUser?.Username ?? "");
            if (ok) UiFeedback.Success($"已修复，作废 {n} 条重复记录");
            else UiFeedback.Warn("记录已修复，但数据库保护仍未生效，详情见日志。");
        }
        catch (Exception ex)
        {
            UiFeedback.Warn($"修复失败：{ex.GetBaseException().Message}");
        }
        Rebuild();
    }

    private static Border ShortcutCard(string title, string desc, Action go)
    {
        var b = new Border
        {
            Width = 200, Height = 88, Margin = new Thickness(0, 0, 12, 12),
            Background = Brushes.White, CornerRadius = new CornerRadius(10),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14),
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = UiTheme.Brush(UiTheme.Text) },
                    new TextBlock
                    {
                        Text = desc, FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
                        Foreground = UiTheme.Brush(UiTheme.Muted),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        b.MouseEnter += (_, _) =>
        {
            b.BorderBrush = UiTheme.Brush(UiTheme.Primary);
            b.Background = UiTheme.Brush(UiTheme.PrimarySoft);
        };
        b.MouseLeave += (_, _) =>
        {
            b.BorderBrush = UiTheme.Brush(UiTheme.Border);
            b.Background = Brushes.White;
        };
        b.MouseLeftButtonUp += (_, _) => go();
        return b;
    }

    private static Border DeviceCard(string name, DeviceStatus status, Action reconnect)
    {
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeights.SemiBold },
                new TextBlock
                {
                    Text = status.Text,
                    Foreground = new SolidColorBrush(status.Color),
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        if (status.NeedsAttention)
        {
            var retry = UiTheme.GhostButton("重连");
            retry.Margin = new Thickness(0, 8, 0, 0);
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Click += (_, _) => reconnect();
            body.Children.Add(retry);
        }
        var card = UiTheme.Card(body, new Thickness(14));
        card.Width = 240;
        card.Margin = new Thickness(0, 0, 12, 0);
        return card;
    }

    private static Border Stat(string title, string value, string? note = null)
    {
        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontSize = 12, Foreground = UiTheme.Brush(UiTheme.Muted) },
                new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) }
            }
        };
        if (note is not null)
            panel.Children.Add(new TextBlock { Text = note, FontSize = UiTheme.FontCaption, Foreground = UiTheme.Brush(UiTheme.Muted), Margin = new Thickness(0, 2, 0, 0) });
        var card = UiTheme.Card(panel, new Thickness(14));
        card.Width = 160;
        card.Margin = new Thickness(0, 0, 12, 0);
        return card;
    }

    private static TextBlock SectionTitle(string t) => new()
    {
        Text = t, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10),
        Foreground = UiTheme.Brush(UiTheme.Text)
    };

    private static string Greeting()
    {
        var h = DateTime.Now.Hour;
        if (h < 12) return "上午好";
        if (h < 18) return "下午好";
        return "晚上好";
    }
}
