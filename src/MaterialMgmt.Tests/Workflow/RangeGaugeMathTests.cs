using MaterialMgmt.Core.Workflow;

namespace MaterialMgmt.Tests.Workflow;

public class RangeGaugeMathTests
{
    [Fact]
    public void Value_InMiddle_IsInZone_AndCentered()
    {
        var g = RangeGaugeMath.Compute(10, 20, 15);
        Assert.Equal(GaugeZone.In, g.Zone);
        Assert.Equal(0.5, g.Position, 6);
        Assert.Equal(2.5, g.DomainMin, 6);
        Assert.Equal(27.5, g.DomainMax, 6);
    }

    [Fact]
    public void Value_BelowMin_IsBelowZone()
    {
        var g = RangeGaugeMath.Compute(10, 20, 5);
        Assert.Equal(GaugeZone.Below, g.Zone);
        Assert.True(g.Position < g.RangeStart);
    }

    [Fact]
    public void Value_AboveMax_IsAboveZone()
    {
        var g = RangeGaugeMath.Compute(10, 20, 25);
        Assert.Equal(GaugeZone.Above, g.Zone);
        Assert.True(g.Position > g.RangeEnd);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void Boundaries_AreInclusive(double value)
    {
        Assert.Equal(GaugeZone.In, RangeGaugeMath.Compute(10, 20, value).Zone);
    }

    [Fact]
    public void FarOutside_ClampsPosition()
    {
        Assert.Equal(0, RangeGaugeMath.Compute(10, 20, -500).Position);
        Assert.Equal(1, RangeGaugeMath.Compute(10, 20, 9999).Position);
    }

    [Fact]
    public void ZeroMin_DomainStartsAtZero()
    {
        var g = RangeGaugeMath.Compute(0, 10, 5);
        Assert.Equal(0, g.DomainMin);
        Assert.Equal(0, g.RangeStart);
        Assert.Equal(GaugeZone.In, g.Zone);
    }

    [Fact]
    public void ZeroSpan_DoesNotDivideByZero()
    {
        var g = RangeGaugeMath.Compute(5, 5, 5);
        Assert.Equal(GaugeZone.In, g.Zone);
        Assert.InRange(g.Position, 0, 1);
    }

    [Fact]
    public void ReversedLimits_AreNormalized()
    {
        var g = RangeGaugeMath.Compute(20, 10, 15);
        Assert.Equal(GaugeZone.In, g.Zone);
        Assert.Equal(0.5, g.Position, 6);
    }

    [Fact]
    public void NaN_IsTreatedAsBelow_AtLeftEdge()
    {
        var g = RangeGaugeMath.Compute(10, 20, double.NaN);
        Assert.Equal(GaugeZone.Below, g.Zone);
        Assert.Equal(0, g.Position);
    }
}
