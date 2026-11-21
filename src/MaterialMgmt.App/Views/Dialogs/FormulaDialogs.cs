using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Themes;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Core.Formulas;
using MaterialMgmt.Infrastructure.Data;
using MaterialMgmt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>新建配方：名称 + 编码（默认自动分配，可手工指定或扫码填入）。成功后 <see cref="CreatedId"/> 为新配方 Id。</summary>
public sealed class FormulaCreateDialog : DialogWindow
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly string? _createdBy;
    private readonly TextBox _name;
    private readonly CheckBox _manual = new() { Content = "手工指定编码", Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _code;

    public int CreatedId { get; private set; }
    public string CreatedCode { get; private set; } = "";

    public FormulaCreateDialog(IDbContextFactory<MaterialMgmtDbContext> dbFactory, DeviceHub devices, string? createdBy)
        : base("新建配方", 420, "创建并编辑步骤")
    {
        _dbFactory = dbFactory;
        _createdBy = createdBy;

        _name = AddField("名称 *", new TextBox { MaxLength = 100 });
        Body.Children.Add(_manual);
        _code = AddField("编码（可扫码填入）", new TextBox { IsEnabled = false, Text = PreviewCode(dbFactory) });
        _manual.Checked += (_, _) => _code.IsEnabled = true;
        _manual.Unchecked += (_, _) =>
        {
            _code.IsEnabled = false;
            _code.Text = PreviewCode(_dbFactory);
        };
        BarcodeFillHelper.AttachDialog(this, devices, _manual, _code);
    }

    internal static string PreviewCode(IDbContextFactory<MaterialMgmtDbContext> dbFactory)
    {
        using var db = dbFactory.CreateDbContext();
        var last = db.Formulas.OrderByDescending(f => f.Id).Select(f => (int?)f.Id).FirstOrDefault();
        return CodeService.PreviewNext(CodeKind.Formula, last);
    }

    protected override void OnConfirm()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { ShowError(_name, "请填写名称"); return; }
        if (_manual.IsChecked == true && string.IsNullOrWhiteSpace(_code.Text))
        {
            ShowError(_code, "请填写编码，或取消“手工指定编码”");
            return;
        }
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var f = new Formula
            {
                Name = _name.Text.Trim(),
                CreatedBy = _createdBy,
                CreatedAt = DateTime.Now,
                IsActive = true,
                Code = ResolveCode(db)
            };
            db.Formulas.Add(f);
            db.SaveChanges();
            CreatedId = f.Id;
            CreatedCode = f.Code;
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_manual.IsChecked == true ? _code : _name, ex.GetBaseException().Message);
        }
    }

    private string ResolveCode(MaterialMgmtDbContext db)
    {
        if (_manual.IsChecked == true)
        {
            return CodeService.AssertUnique(Array.Empty<string>(), _code.Text, null,
                db.Formulas.Select(x => new ValueTuple<int, string>(x.Id, x.Code)).AsEnumerable());
        }
        var nextId = (db.Formulas.Select(x => (int?)x.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Formula, nextId, (c, _) => !db.Formulas.Any(x => x.Code == c));
    }
}

/// <summary>复制配方：填新名称，自动分配编码并复制全部步骤。</summary>
public sealed class FormulaCopyDialog : DialogWindow
{
    private readonly FormulaService _formulas;
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly int _sourceId;
    private readonly string? _createdBy;
    private readonly TextBox _name;

    public int CreatedId { get; private set; }
    public string CreatedCode { get; private set; } = "";
    public string CreatedName { get; private set; } = "";

    public FormulaCopyDialog(FormulaService formulas, IDbContextFactory<MaterialMgmtDbContext> dbFactory,
        Formula source, string? createdBy)
        : base("复制配方", 440, "创建副本并编辑")
    {
        _formulas = formulas;
        _dbFactory = dbFactory;
        _sourceId = source.Id;
        _createdBy = createdBy;

        var src = UiTheme.Hint($"源配方：{source.Code}  {source.Name}");
        src.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(src);
        _name = AddField("新名称 *", new TextBox { Text = FormulaClone.DefaultCopyName(source.Name), MaxLength = 100 },
            $"将自动分配编码（预览 {FormulaCreateDialog.PreviewCode(dbFactory)}），并复制全部称量步骤");
        Loaded += (_, _) => _name.SelectAll();
    }

    protected override void OnConfirm()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { ShowError(_name, "请填写新名称"); return; }
        try
        {
            CreatedId = _formulas.Clone(_sourceId, _name.Text, _createdBy);
            using var db = _dbFactory.CreateDbContext();
            var created = db.Formulas.First(f => f.Id == CreatedId);
            CreatedCode = created.Code;
            CreatedName = created.Name;
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_name, ex.GetBaseException().Message);
        }
    }
}
