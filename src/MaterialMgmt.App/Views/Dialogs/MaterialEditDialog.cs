using System.Windows;
using System.Windows.Controls;
using MaterialMgmt.App.Services;
using MaterialMgmt.Core.Entities;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.App.Views.Dialogs;

/// <summary>新增 / 编辑原材料。编码默认自动分配，勾选“手工指定编码”后可手填或扫码填入。</summary>
public sealed class MaterialEditDialog : DialogWindow
{
    private readonly MaterialService _materials;
    private readonly Material? _existing;
    private readonly TextBox _name;
    private readonly TextBox _desc;
    private readonly CheckBox _manual = new() { Content = "手工指定编码", Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _code;

    public string SavedCode { get; private set; } = "";

    public MaterialEditDialog(MaterialService materials, DeviceHub devices, Material? existing)
        : base(existing is null ? "新增原材料" : "编辑原材料", 420)
    {
        _materials = materials;
        _existing = existing;

        _name = AddField("名称 *", new TextBox { Text = existing?.Name ?? "", MaxLength = 100 });
        _desc = AddField("描述", new TextBox { Text = existing?.Description ?? "", MaxLength = 200 });
        Body.Children.Add(_manual);
        _code = AddField("编码（可扫码填入）", new TextBox { IsEnabled = false, Text = existing?.Code ?? _materials.PreviewNextCode() });

        _manual.Checked += (_, _) => _code.IsEnabled = true;
        _manual.Unchecked += (_, _) =>
        {
            _code.IsEnabled = false;
            _code.Text = _existing?.Code ?? _materials.PreviewNextCode();
        };

        BarcodeFillHelper.AttachDialog(this, devices, _manual, _code);
    }

    protected override void OnConfirm()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            ShowError(_name, "请填写名称");
            return;
        }
        var manualCode = _manual.IsChecked == true ? _code.Text : null;
        if (manualCode is not null && string.IsNullOrWhiteSpace(manualCode))
        {
            ShowError(_code, "请填写编码，或取消“手工指定编码”");
            return;
        }

        try
        {
            var saved = _existing is null
                ? _materials.Create(_name.Text, _desc.Text, manualCode)
                : _materials.Update(_existing.Id, _name.Text, _desc.Text, manualCode);
            SavedCode = saved.Code;
            Accept();
        }
        catch (Exception ex)
        {
            ShowError(manualCode is not null ? _code : _name, ex.GetBaseException().Message);
        }
    }
}
