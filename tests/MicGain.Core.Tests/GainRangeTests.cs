using MicGain.Core.Models;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class GainRangeTests
{
    [Theory]
    [InlineData(-100, GainRange.MinDb)]
    [InlineData(100, GainRange.MaxDb)]
    [InlineData(0, 0)]
    [InlineData(-12.5, -12.5)]
    [InlineData(GainRange.MinDb, GainRange.MinDb)]
    [InlineData(GainRange.MaxDb, GainRange.MaxDb)]
    public void Clamp_ConstrainsToAllowedPreampRange(double input, double expected) =>
        Assert.Equal(expected, GainRange.Clamp(input));

    [Fact]
    public void Clamp_NaN_FallsBackToDefault() =>
        Assert.Equal(GainRange.DefaultDb, GainRange.Clamp(double.NaN));
}
