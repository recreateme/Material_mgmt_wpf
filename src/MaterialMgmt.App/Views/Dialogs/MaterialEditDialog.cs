using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.Core.Codes;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>新增 / 编辑原材料。编码默认自动分配，勾选“手工指定编码”后可手填或扫码填入。</summary>
public sealed class MaterialEditDialog : DialogWindow
{
    private readonly IDbContextFactory<MaterialMgmtDbContext> _dbFactory;
    private readonly Material? _existing;
    private readonly TextBox _name;
    private readonly TextBox _desc;
    private readonly CheckBox _manual = new() { Content = "手工指定编码", Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _code;

    public string SavedCode { get; private set; } = "";

    public MaterialEditDialog(IDbContextFactory<MaterialMgmtDbContext> dbFactory, DeviceHub devices, Material? existing)
        : base(existing is null ? "新增原材料" : "编辑原材料", 420)
    {
        _dbFactory = dbFactory;
        _existing = existing;

        _name = AddField("名称 *", new TextBox { Text = existing?.Name ?? "", MaxLength = 100 });
        _desc = AddField("描述", new TextBox { Text = existing?.Description ?? "", MaxLength = 200 });
        Body.Children.Add(_manual);
        _code = AddField("编码（可扫码填入）", new TextBox { IsEnabled = false, Text = existing?.Code ?? PreviewCode() });

        _manual.Checked += (_, _) => _code.IsEnabled = true;
        _manual.Unchecked += (_, _) =>
        {
            _code.IsEnabled = false;
            _code.Text = _existing?.Code ?? PreviewCode();
        };

        BarcodeFillHelper.AttachDialog(this, devices, _manual, _code);
    }

    private string PreviewCode()
    {
        using var db = _dbFactory.CreateDbContext();
        var last = db.Materials.OrderByDescending(m => m.Id).Select(m => (int?)m.Id).FirstOrDefault();
        return CodeService.PreviewNext(CodeKind.Material, last);
    }

    protected override void OnConfirm()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            ShowError(_name, "请填写名称");
            return;
        }
        if (_manual.IsChecked == true && string.IsNullOrWhiteSpace(_code.Text))
        {
            ShowError(_code, "请填写编码，或取消“手工指定编码”");
            return;
        }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            Material entity;
            if (_existing is null)
            {
                entity = new Material
                {
                    Name = _name.Text.Trim(),
                    Description = string.IsNullOrWhiteSpace(_desc.Text) ? null : _desc.Text.Trim(),
                    CreatedAt = DateTime.Now,
                    Code = ResolveCreateCode(db)
                };
                db.Materials.Add(entity);
            }
            else
            {
                entity = db.Materials.First(m => m.Id == _existing.Id);
                entity.Name = _name.Text.Trim();
                entity.Description = string.IsNullOrWhiteSpace(_desc.Text) ? null : _desc.Text.Trim();
                if (_manual.IsChecked == true)
                {
                    entity.Code = CodeService.AssertUnique(
                        Array.Empty<string>(), _code.Text, entity.Id,
                        db.Materials.Select(m => new ValueTuple<int, string>(m.Id, m.Code)).AsEnumerable());
                }
            }
            db.SaveChanges();
            SavedCode = entity.Code;
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(_manual.IsChecked == true ? _code : _name, ex.GetBaseException().Message);
        }
    }

    private string ResolveCreateCode(MaterialMgmtDbContext db)
    {
        if (_manual.IsChecked == true)
        {
            return CodeService.AssertUnique(
                Array.Empty<string>(), _code.Text, null,
                db.Materials.Select(m => new ValueTuple<int, string>(m.Id, m.Code)).AsEnumerable());
        }

        var nextId = (db.Materials.Select(m => (int?)m.Id).Max() ?? 0) + 1;
        return CodeService.AssignAuto(CodeKind.Material, nextId,
            (c, _) => !db.Materials.Any(m => m.Code == c));
    }
}
