using MaterialMgmt.Core.Codes;

namespace MaterialMgmt.Tests.Codes;

public class CodeServiceTests
{
    [Fact]
    public void FormatAutoCode()
    {
        Assert.Equal("MAT-000012", CodeService.FormatAutoCode(CodeKind.Material, 12));
        Assert.Equal("FML-000003", CodeService.FormatAutoCode(CodeKind.Formula, 3));
    }

    [Fact]
    public void Validate_RejectsEmpty()
    {
        Assert.Throws<CodeError>(() => CodeService.ValidateFormat(""));
    }

    [Fact]
    public void AssertUnique_DetectsDuplicate()
    {
        Assert.Throws<CodeError>(() =>
            CodeService.AssertUnique(new[] { "MAT-000001" }, "mat-000001"));
    }
}
