using MaterialMgmt.Core.Formulas;

namespace MaterialMgmt.Tests.Formulas;

public class FormulaSequenceTests
{
    [Fact]
    public void TwoPhase_AvoidsDuplicateFinalValuesInFirstWave()
    {
        var ids = new[] { 10, 20, 30 };
        var phases = FormulaSequence.BuildTwoPhase(ids);

        Assert.Equal(6, phases.Count);
        var firstWave = phases.Take(3).Select(x => x.Sequence).ToHashSet();
        Assert.DoesNotContain(1, firstWave);
        Assert.DoesNotContain(2, firstWave);
        Assert.DoesNotContain(3, firstWave);

        Assert.Equal(new[] { 1, 2, 3 }, phases.Skip(3).Select(x => x.Sequence));
        Assert.Equal(ids, phases.Skip(3).Select(x => x.Id));
    }

    [Fact]
    public void TwoPhase_Empty()
    {
        Assert.Empty(FormulaSequence.BuildTwoPhase(Array.Empty<int>()));
    }

    [Theory]
    [InlineData(3, 0, new[] { 4, 1, 2, 3, 5 })]
    [InlineData(0, 5, new[] { 2, 3, 4, 5, 1 })]
    [InlineData(0, 3, new[] { 2, 3, 1, 4, 5 })]
    [InlineData(4, 1, new[] { 1, 5, 2, 3, 4 })]
    [InlineData(2, 2, new[] { 1, 2, 3, 4, 5 })]
    [InlineData(2, 3, new[] { 1, 2, 3, 4, 5 })]
    [InlineData(1, 99, new[] { 1, 3, 4, 5, 2 })]
    [InlineData(1, -3, new[] { 2, 1, 3, 4, 5 })]
    public void MoveTo_PlacesItemAtGap(int from, int gap, int[] expected)
    {
        Assert.Equal(expected, FormulaSequence.MoveTo(new[] { 1, 2, 3, 4, 5 }, from, gap));
    }

    [Fact]
    public void MoveTo_InvalidSource_ReturnsCopy()
    {
        Assert.Equal(new[] { 1, 2 }, FormulaSequence.MoveTo(new[] { 1, 2 }, 5, 0));
    }
}
