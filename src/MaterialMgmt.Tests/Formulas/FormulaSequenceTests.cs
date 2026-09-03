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
}
