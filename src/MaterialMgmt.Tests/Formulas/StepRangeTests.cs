using MaterialMgmt.Core.Formulas;

namespace MaterialMgmt.Tests.Formulas;

public class StepRangeTests
{
    [Fact]
    public void Valid_range_parses()
    {
        Assert.Null(StepRange.Validate(" 9.8 ", "10.5", out var min, out var max));
        Assert.Equal(9.8, min);
        Assert.Equal(10.5, max);
    }

    [Fact]
    public void Equal_bounds_are_allowed()
        => Assert.Null(StepRange.Validate("5", "5", out _, out _));

    [Theory]
    [InlineData("", "10", "下限请输入数字")]
    [InlineData("abc", "10", "下限请输入数字")]
    [InlineData("5", "", "上限请输入数字")]
    [InlineData("0", "10", "下限必须大于 0")]
    [InlineData("-1", "10", "下限必须大于 0")]
    [InlineData("5", "0", "上限必须大于 0")]
    [InlineData("10.5", "9.8", "下限不能大于上限")]
    [InlineData("NaN", "10", "下限请输入数字")]
    public void Invalid_ranges_explain_why(string min, string max, string expected)
        => Assert.Equal(expected, StepRange.Validate(min, max, out _, out _));
}
