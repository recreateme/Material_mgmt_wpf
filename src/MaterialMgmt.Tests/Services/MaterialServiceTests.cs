using MaterialMgmt.Core.Codes;
using MaterialMgmt.Infrastructure.Services;

namespace MaterialMgmt.Tests.Services;

public class MaterialServiceTests : TestHost
{
    private MaterialService Svc => Get<MaterialService>();

    [Fact]
    public void Create_AutoCode_FollowsPreview()
    {
        var preview = Svc.PreviewNextCode();
        var m = Svc.Create("  碳粉  ", "  ", null);
        Assert.Equal(preview, m.Code);
        Assert.Equal("碳粉", m.Name);
        Assert.Null(m.Description);
    }

    [Fact]
    public void Create_DuplicateManualCode_Throws()
    {
        Svc.Create("甲", null, "MAT-X00001");
        Assert.Throws<CodeError>(() => Svc.Create("乙", null, "MAT-X00001"));
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Svc.Create(" ", null, null));
    }

    [Fact]
    public void Update_KeepsCode_WhenManualCodeNull()
    {
        var m = Svc.Create("甲", null, "MAT-X00002");
        var u = Svc.Update(m.Id, "甲改", "说明", null);
        Assert.Equal("MAT-X00002", u.Code);
        Assert.Equal("甲改", u.Name);
        Assert.Equal("说明", u.Description);
    }

    [Fact]
    public void Update_ToOwnCode_IsAllowed()
    {
        var m = Svc.Create("甲", null, "MAT-X00003");
        Svc.Update(m.Id, "甲", null, "MAT-X00003");
        Assert.Equal("MAT-X00003", Svc.All().Single(x => x.Id == m.Id).Code);
    }

    [Fact]
    public void DeleteMany_ReferencedMaterial_DeletesNothing()
    {
        var used = Svc.Create("用到的", null, null);
        var free = Svc.Create("没用到的", null, null);
        AddFormula("FML-T00001", (used, 1, 2));

        Assert.Equal(new[] { used.Id }, Svc.ReferencedByFormulas(new[] { used.Id, free.Id }).Select(m => m.Id));
        Assert.Throws<InvalidOperationException>(() => Svc.DeleteMany(new[] { used.Id, free.Id }));
        Assert.Equal(2, Svc.All().Count);

        Svc.DeleteMany(new[] { free.Id });
        Assert.Equal(new[] { used.Id }, Svc.All().Select(m => m.Id));
    }
}
