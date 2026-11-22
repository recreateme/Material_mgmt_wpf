using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.App.Views.Panels;

/// <summary>开始称量：选配方或扫 FML- 码，填写工艺参数后调用 OrderService.Create。</summary>
public sealed class SessionSelectPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly FormulaService _formulaService;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly NavigationService _nav;
    private readonly IConfigService _config;
    private readonly OrderService _orders;
    private readonly ComboBox _formulas = new() { Width = 360, MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _activeBanner = new();
    private readonly TextBlock _previewTitle = new()
    {
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 6)
    };
    private readonly TextBlock _previewHint = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
        Foreground = UiTheme.Brush(UiTheme.Warning),
        FontSize = 13
    };
    private readonly DataGrid _preview = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        MaxHeight = 320,
        MaxWidth = 720,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly Button _start;
    private List<Formula> _list = new();

    public SessionSelectPanel(FormulaService formulas, IAuthService auth,
        IAuditLog audit, NavigationService nav, IConfigService config, OrderService orders)
    {
        _formulaService = formulas; _auth = auth; _audit = audit; _nav = nav; _config = config; _orders = orders;
        _start = UiTheme.PrimaryButton("填写工艺参数并开始");
        Build();
    }

    public void OnShow()
    {
        LoadActiveOrders();
        LoadFormulas();
    }

    public void OnBarcode(string code, bool simulated = false)
    {
        if (!CodeService.IsFormulaCode(code)) return;
        var f = _list.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (f is null) { UiFeedback.Hint($"未找到启用的配方：{code}"); return; }
        _formulas.SelectedItem = _formulas.Items.Cast<FormulaOption>().FirstOrDefault(x => x.Id == f.Id);
    }

    private void Build()
    {
        var form = new StackPanel { Margin = new Thickness(24, 24, 24, 8) };
        form.Children.Add(new TextBlock { Text = "开始称量", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
        form.Children.Add(_activeBanner);
        form.Children.Add(UiTheme.FieldLabel("选择配方"));
        form.Children.Add(UiTheme.Hint("下拉选择或扫描 FML- 配方码"));
        _formulas.DisplayMemberPath = "Display";
        _formulas.SelectionChanged += (_, _) => Preview();
        form.Children.Add(_formulas);

        _preview.Columns.Add(GridKit.Text("序号", "Sequence", 56));
        _preview.Columns.Add(GridKit.Text("原料编码", "Code", 130));
        _preview.Columns.Add(GridKit.Text("原料名称", "Name", new DataGridLength(1, DataGridLengthUnitType.Star), align: TextAlignment.Left));
        _preview.Columns.Add(GridKit.Text("下限(g)", "Min", 100, "F3"));
        _preview.Columns.Add(GridKit.Text("上限(g)", "Max", 100, "F3"));
        form.Children.Add(_previewTitle);
        form.Children.Add(_preview);
        form.Children.Add(_previewHint);

        _start.Height = 40;
        _start.Width = 220;
        _start.Margin = new Thickness(0);
        _start.HorizontalAlignment = HorizontalAlignment.Left;
        _start.Click += (_, _) => Start();
        var footer = new StackPanel { Margin = new Thickness(24, 8, 24, 24) };
        footer.Children.Add(_start);

        Content = UiTheme.DialogBody(form, footer);
    }

    private void LoadFormulas()
    {
        _list = _formulaService.All(activeOnly: true).OrderBy(f => f.Code).ToList();
        _formulas.ItemsSource = _list.Select(f => new FormulaOption(f)).ToList();
        if (_formulas.Items.Count > 0) _formulas.SelectedIndex = 0;
        else Preview();
    }

    /// <summary>本人未完成的工单放在最上方，避免重复开新单。</summary>
    private void LoadActiveOrders()
    {
        _activeBanner.Children.Clear();
        var user = _auth.CurrentUser;
        if (user is null) return;
        var mine = _orders.ActiveOrders(user.Id);
        if (mine.Count == 0) return;

        var list = new StackPanel();
        list.Children.Add(new TextBlock
        {
            Text = $"你有 {mine.Count} 个未完成的工单，可直接继续：",
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Warning),
            Margin = new Thickness(0, 0, 0, 6)
        });
        foreach (var o in mine)
        {
            var go = UiTheme.GhostButton("继续");
            go.Margin = new Thickness(12, 0, 0, 0);
            var (id, formulaId) = (o.Id, o.FormulaId);
            go.Click += (_, _) => _nav.Navigate(AppPage.WeighSession, v => ((WeighSessionPanel)v).Load(id, formulaId));
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            row.Children.Add(new TextBlock
            {
                Text = $"#{o.Id}  {o.FormulaCode} {o.FormulaName}  ·  开始于 {o.StartTime:MM-dd HH:mm}  ·  已完成 {o.DoneSteps} / {o.TotalSteps} 步",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            list.Children.Add(row);
        }
        var card = UiTheme.Card(list, new Thickness(16, 12, 12, 12));
        card.Background = UiTheme.Brush(UiTheme.WarningSoft);
        card.BorderBrush = UiTheme.Brush(UiTheme.Warning);
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(0, 0, 0, 16);
        _activeBanner.Children.Add(card);
    }

    private void Preview()
    {
        _preview.ItemsSource = null;
        if (_formulas.SelectedItem is not FormulaOption opt)
        {
            ShowPreviewProblem("暂无可用配方，请联系管理员配置。");
            return;
        }

        var items = (_formulaService.GetWithSteps(opt.Id)?.Items ?? [])
            .Select(i => new PreviewRow(i.Sequence, i.Material.Code, i.Material.Name, i.WeightMin, i.WeightMax))
            .ToList();
        if (items.Count == 0)
        {
            ShowPreviewProblem("该配方尚无称量步骤，请先在配方编辑器中配置。");
            return;
        }

        _previewTitle.Text = $"称量步骤预览（共 {items.Count} 步）";
        _previewTitle.Visibility = Visibility.Visible;
        _preview.ItemsSource = items;
        _preview.Visibility = Visibility.Visible;
        _previewHint.Visibility = Visibility.Collapsed;
        _start.IsEnabled = true;
    }

    private void ShowPreviewProblem(string message)
    {
        _previewTitle.Visibility = Visibility.Collapsed;
        _preview.Visibility = Visibility.Collapsed;
        _previewHint.Text = message;
        _previewHint.Visibility = Visibility.Visible;
        _start.IsEnabled = false;
    }

    private sealed record PreviewRow(int Sequence, string Code, string Name, double Min, double Max);

    private void Start()
    {
        if (_formulas.SelectedItem is not FormulaOption opt)
        {
            UiFeedback.Hint("请先选择配方");
            return;
        }
        if (!_orders.FormulaHasSteps(opt.Id))
        {
            UiFeedback.Hint("该配方没有步骤，无法开始称量");
            return;
        }

        var dlg = new WorkOrderParamsDialog(_config);
        if (dlg.ShowDialog() != true || dlg.Params is null) return;
        var user = _auth.CurrentUser!;

        int id;
        try { id = _orders.Create(opt.Id, user.Id, user.DisplayName, dlg.Params); }
        catch (InvalidOperationException ex) { UiFeedback.Warn(ex.Message); LoadFormulas(); return; }
        _audit.Log("order", $"创建工单 #{id} 配方={opt.Code}", user.Username);
        _nav.Navigate(AppPage.WeighSession, v => ((WeighSessionPanel)v).Load(id, opt.Id));
    }

    private sealed class FormulaOption
    {
        public FormulaOption(Formula f) { Id = f.Id; Code = f.Code; Display = $"{f.Code}  {f.Name}"; }
        public int Id { get; }
        public string Code { get; }
        public string Display { get; }
    }
}
