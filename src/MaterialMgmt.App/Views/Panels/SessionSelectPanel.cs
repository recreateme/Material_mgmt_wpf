using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Orders;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Panels;

public sealed class SessionSelectPanel : UserControl, IPageLifecycle, IBarcodeHandler
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IAuditLog _audit;
    private readonly NavigationService _nav;
    private readonly ComboBox _formulas = new() { Height = 32, Width = 360 };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private List<Formula> _list = new();

    public SessionSelectPanel(IDbContextFactory<MaterialMgmtDbContext> dbFactory, IAuthService auth,
        IAuditLog audit, NavigationService nav)
    {
        _dbFactory = dbFactory; _auth = auth; _audit = audit; _nav = nav;
        Build();
    }

    public void OnShow() => LoadFormulas();

    public void OnBarcode(string code, bool simulated = false)
    {
        if (!CodeService.IsFormulaCode(code)) return;
        var f = _list.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (f is null) { MessageBox.Show($"未找到配方：{code}"); return; }
        _formulas.SelectedItem = f;
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "开始称量", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(new TextBlock { Text = "选择配方（或扫描 FML- 配方码）", Margin = new Thickness(0, 0, 0, 8) });
        _formulas.DisplayMemberPath = "Display";
        _formulas.SelectionChanged += (_, _) => Preview();
        root.Children.Add(_formulas);
        root.Children.Add(_preview);
        var start = UiTheme.PrimaryButton("填写工艺参数并开始");
        start.Height = 36;
        start.Width = 220;
        start.Margin = new Thickness(0, 16, 0, 0);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        start.Click += (_, _) => Start();
        root.Children.Add(start);
        Content = root;
    }

    private void LoadFormulas()
    {
        using var db = _dbFactory.CreateDbContext();
        _list = db.Formulas.Where(f => f.IsActive).OrderBy(f => f.Code).ToList();
        _formulas.ItemsSource = _list.Select(f => new FormulaOption(f)).ToList();
        if (_formulas.Items.Count > 0) _formulas.SelectedIndex = 0;
    }

    private void Preview()
    {
        if (_formulas.SelectedItem is not FormulaOption opt) { _preview.Text = ""; return; }
        using var db = _dbFactory.CreateDbContext();
        var items = db.FormulaItems.Include(i => i.Material).Where(i => i.FormulaId == opt.Id)
            .OrderBy(i => i.Sequence).ToList();
        _preview.Text = items.Count == 0
            ? "该配方尚无称量步骤，请先在配方编辑器中配置。"
            : "称量步骤：\n" + string.Join("\n", items.Select(i =>
                $"{i.Sequence}. {i.Material.Code} {i.Material.Name}  [{i.WeightMin:F3} ~ {i.WeightMax:F3} g]"));
    }

    private void Start()
    {
        if (_formulas.SelectedItem is not FormulaOption opt) return;
        using (var db = _dbFactory.CreateDbContext())
        {
            if (!db.FormulaItems.Any(i => i.FormulaId == opt.Id))
            {
                MessageBox.Show("该配方没有步骤，无法开始称量");
                return;
            }
        }

        var dlg = new WorkOrderParamsDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.Params is null) return;
        var p = dlg.Params;
        var user = _auth.CurrentUser!;

        using var db2 = _dbFactory.CreateDbContext();
        var wo = new WorkOrder
        {
            FormulaId = opt.Id,
            OperatorId = user.Id,
            Operator = user.DisplayName,
            StartTime = DateTime.Now,
            Status = OrderRules.InProgress,
            Temperature = p.Temperature,
            Humidity = p.Humidity,
            ProductionNo = p.ProductionNo,
            D50 = p.D50,
            MixingException = p.MixingException,
            MixingExceptionNote = p.MixingExceptionNote,
            BaseFilm = p.BaseFilm,
            BatchNo = p.BatchNo,
            Thickness = p.Thickness,
            CoatingException = p.CoatingException,
            CoatingExceptionNote = p.CoatingExceptionNote
        };
        db2.WorkOrders.Add(wo);
        db2.SaveChanges();
        _audit.Log("order", $"创建工单 #{wo.Id} 配方={opt.Code}", user.Username);
        _nav.Navigate(AppPage.WeighSession, v => ((WeighSessionPanel)v).Load(wo.Id, opt.Id));
    }

    private sealed class FormulaOption
    {
        public FormulaOption(Formula f) { Id = f.Id; Code = f.Code; Display = $"{f.Code}  {f.Name}"; }
        public int Id { get; }
        public string Code { get; }
        public string Display { get; }
    }
}
