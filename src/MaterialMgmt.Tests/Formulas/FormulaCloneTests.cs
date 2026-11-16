using MaterialMgmt.Core.Formulas;

namespace MaterialMgmt.Tests.Formulas;

public class FormulaCloneTests
{
    [Fact]
    public void DefaultCopyName_AppendsSuffix()
    {
        Assert.Equal("4K模拟 - 副本", FormulaClone.DefaultCopyName("4K模拟"));
    }

    [Fact]
    public void DefaultCopyName_DoesNotDoubleSuffix()
    {
        Assert.Equal("4K模拟 - 副本", FormulaClone.DefaultCopyName("4K模拟 - 副本"));
    }

    [Fact]
    public void DefaultCopyName_EmptyFallback()
    {
        Assert.Equal("未命名配方 - 副本", FormulaClone.DefaultCopyName("  "));
    }

    [Fact]
    public void SnapshotSteps_OrdersBySequence()
    {
        var snaps = FormulaClone.SnapshotSteps(new[]
        {
            (2, 2, 1.0, 2.0, (string?)null),
            (1, 1, 0.5, 1.0, "a"),
        });
        Assert.Equal(new[] { 1, 2 }, snaps.Select(s => s.Sequence));
        Assert.Equal(1, snaps[0].MaterialId);
        Assert.Equal("a", snaps[0].Notes);
    }
}
