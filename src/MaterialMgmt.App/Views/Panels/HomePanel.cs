using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Auth;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace MaterialMgmt.App.Views.Panels;

public sealed class HomePanel : UserControl, IPageLifecycle
{
    private readonly IAuthService _auth;
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly NavigationService _nav;
    private readonly WeighWorkspace _weigh;
    private readonly DeviceHub _devices;
    private readonly DbBootstrap _bootstrap;
    private readonly OrderService _orders;
    private readonly IAuditLog _audit;
    private readonly StackPanel _body = new() { Margin = new Thickness(24) };

    public HomePanel(IAuthService auth, IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        NavigationService nav, WeighWorkspace weigh, DeviceHub devices,
        DbBootstrap bootstrap, OrderService orders, IAuditLog audit)
    {
        _auth = auth;
        _dbFactory = dbFactory;
        _nav = nav;
        _weigh = weigh;
        _devices = devices;
        _bootstrap = bootstrap;
        _orders = orders;
        _audit = audit;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _devices.ScaleStatusChanged += () => Dispatcher.Invoke(RefreshDevices);
        _devices.ScannerStatusChanged += () => Dispatcher.Invoke(RefreshDevices);
    }

    public void OnShow() => Rebuild();

    private void Rebuild()
    {
        _body.Children.Clear();
        var user = _auth.CurrentUser;
        if (user is null) return;

        var banner = new Border
        {
            Background = new LinearGradientBrush(UiTheme.BannerStart, UiTheme.BannerEnd, 0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(28, 24, 28, 24),
            Margin = new Thickness(0, 0, 0, 18)
        };
        banner.Child = new StackPanel
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
                    Text = $"欢迎回来，今天是 {DateTime.Now:yyyy年M月d日 dddd}",
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
        _body.Children.Add(banner);

        if (user.IsBusiness())
            AddResumeWorkOrderCard(user);

        _body.Children.Add(SectionTitle("快捷入口"));
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        void Card(string title, string desc, Action go, bool show)
        {
            if (!show) return;
            var b = new Border
            {
                Width = 200, Height = 96, Margin = new Thickness(0, 0, 12, 12),
                Background = Brushes.White, CornerRadius = new CornerRadius(10),
                BorderBrush = UiTheme.Brush(UiTheme.Border),
                BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14),
                Cursor = Cursors.Hand
            };
            b.Child = new StackPanel
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
            };
            b.MouseEnter += (_, _) =>
            {
                b.BorderBrush = UiTheme.Brush(UiTheme.Primary);
                b.BorderThickness = new Thickness(2);
                b.Background = UiTheme.Brush(UiTheme.PrimarySoft);
            };
            b.MouseLeave += (_, _) =>
            {
                b.BorderBrush = UiTheme.Brush(UiTheme.Border);
                b.BorderThickness = new Thickness(1);
                b.Background = Brushes.White;
            };
            b.MouseLeftButtonUp += (_, _) => go();
            cards.Children.Add(b);
        }

        var biz = user.IsBusiness();
        Card("打开称量窗", "开始称量 / 我的工单（独立窗口）", () => _weigh.Open(AppPage.SessionSelect), biz);
        Card("原材料", "维护原材料主数据", () => _nav.Navigate(AppPage.Materials), biz);
        Card("配方管理", "维护配方与称量步骤", () => _nav.Navigate(AppPage.Formulas), biz);
        Card("工单历史", "查看与导出全部工单", () => _nav.Navigate(AppPage.AdminHistory), biz);
        Card("我的工单", "在称量窗中查看与恢复", () => _weigh.Open(AppPage.OperatorHistory), biz);
        Card("用户管理", "维护业务人员账号", () => _nav.Navigate(AppPage.UserMgmt), user.IsSystemAdmin());
        Card("系统设置", "串口、扫码与改密", () => _nav.Navigate(AppPage.Settings), true);
        _body.Children.Add(cards);

        _body.Children.Add(SectionTitle("概览统计"));
        var stats = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        using (var db = _dbFactory.CreateDbContext())
        {
            if (biz)
            {
                stats.Children.Add(Stat("原材料", db.Materials.Count().ToString()));
                stats.Children.Add(Stat("启用配方", db.Formulas.Count(f => f.IsActive).ToString()));
                stats.Children.Add(Stat("全部工单", db.WorkOrders.Count(w => w.DeletedAt == null).ToString()));
                stats.Children.Add(Stat("我的工单", db.WorkOrders.Count(w => w.OperatorId == user.Id && w.DeletedAt == null).ToString()));
                stats.Children.Add(Stat("进行中",
                    db.WorkOrders.Count(w => w.Status == "in_progress" && w.DeletedAt == null).ToString()));
            }
            else
            {
                stats.Children.Add(Stat("业务人员",
                    db.Users.Count(u => u.Role == Roles.Business).ToString()));
                stats.Children.Add(Stat("启用账号",
                    db.Users.Count(u => u.Role == Roles.Business && u.IsActive).ToString()));
            }
        }
        _body.Children.Add(stats);

        _body.Children.Add(SectionTitle("设备状态"));
        _deviceRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        _body.Children.Add(_deviceRow);
        RefreshDevices();

        if (user.IsSystemAdmin())
            AddDataSafetySection();
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

    private void AddResumeWorkOrderCard(CurrentUser user)
    {
        using var db = _dbFactory.CreateDbContext();
        var wip = db.WorkOrders.Include(w => w.Formula)
            .Where(w => w.OperatorId == user.Id && w.Status == OrderRules.InProgress)
            .OrderByDescending(w => w.Id)
            .FirstOrDefault();
        if (wip is null) return;

        var formulaName = $"{wip.Formula.Code} {wip.Formula.Name}";
        var card = UiTheme.Card(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "进行中的工单",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 15,
                    Foreground = UiTheme.Brush(UiTheme.Warning)
                },
                new TextBlock
                {
                    Text = $"工单 #{wip.Id}  ·  {formulaName}",
                    Margin = new Thickness(0, 8, 0, 4),
                    Foreground = UiTheme.Brush(UiTheme.Text)
                },
                new TextBlock
                {
                    Text = "点击继续称量",
                    FontSize = 12,
                    Foreground = UiTheme.Brush(UiTheme.Primary)
                }
            }
        }, new Thickness(18));
        card.Margin = new Thickness(0, 0, 0, 16);
        card.Cursor = Cursors.Hand;
        card.BorderBrush = UiTheme.Brush(UiTheme.Warning);
        card.MouseLeftButtonUp += (_, _) => _weigh.OpenResume(wip.Id, wip.FormulaId);
        _body.Children.Add(card);
    }

    private WrapPanel _deviceRow = new();

    private void RefreshDevices()
    {
        if (_deviceRow.Parent is null) return;
        _deviceRow.Children.Clear();
        _deviceRow.Children.Add(DeviceCard("电子天平",
            _devices.IsScaleConnected ? $"已连接（{_devices.ConnectedPort}）" : ScaleStatusText(),
            _devices.IsScaleConnected ? UiTheme.Success : UiTheme.Danger));
        _deviceRow.Children.Add(DeviceCard("扫码枪", ScannerStatusText(), ScannerStatusColor()));
    }

    private string ScaleStatusText()
    {
        if (_devices.IsDiscovering || _devices.ScaleLastError.Contains("识别", StringComparison.Ordinal))
            return _devices.ScaleLastError;
        return string.IsNullOrEmpty(_devices.ScaleLastError) ? "未连接" : $"未连接 — {_devices.ScaleLastError}";
    }

    private string ScannerStatusText()
    {
        var label = _devices.ScannerSourceLabel;
        if (_devices.IsScannerSerialConnected)
            return $"已连接（{label}）";
        if (!string.IsNullOrEmpty(_devices.ScannerLastError))
            return $"{_devices.ScannerLastError}（{label}）";
        return _devices.IsScannerListening ? $"监听中（{label}）" : $"空闲（{label}）";
    }

    private static Color ScannerStatusColor(DeviceHub devices)
    {
        var label = devices.ScannerSourceLabel;
        var ok = devices.IsScannerSerialConnected
                 || label.Contains("HID", StringComparison.Ordinal)
                 || label.Contains("关闭", StringComparison.Ordinal);
        if (ok && string.IsNullOrEmpty(devices.ScannerLastError))
            return UiTheme.Success;
        return devices.IsScannerListening ? UiTheme.Warning : UiTheme.Danger;
    }

    private Color ScannerStatusColor() => ScannerStatusColor(_devices);

    private static Border DeviceCard(string name, string status, Color color)
    {
        var card = UiTheme.Card(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = status, Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 6, 0, 0) }
            }
        }, new Thickness(14));
        card.Width = 180;
        card.Margin = new Thickness(0, 0, 12, 0);
        return card;
    }

    private static Border Stat(string title, string value)
    {
        var card = UiTheme.Card(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontSize = 12, Foreground = UiTheme.Brush(UiTheme.Muted) },
                new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) }
            }
        }, new Thickness(14));
        card.Width = 140;
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
