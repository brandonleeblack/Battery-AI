using Battery.Core;

namespace Battery.Tests;

public class PaceCalculatorTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    static UsageWindow W(double used, double hoursLeft, double lengthHours = 5) =>
        new(WindowKind.FiveHour, used, Now.AddHours(hoursLeft), TimeSpan.FromHours(lengthHours));

    [Fact]
    public void Elapsed_is_share_of_window_gone() =>
        Assert.Equal(55, PaceCalculator.ElapsedPercent(W(0, 2.25), Now), 3);

    [Fact]
    public void Elapsed_clamps_when_reset_is_past() =>
        Assert.Equal(100, PaceCalculator.ElapsedPercent(W(0, -1), Now));

    [Fact]
    public void Elapsed_clamps_when_reset_is_further_than_window() =>
        Assert.Equal(0, PaceCalculator.ElapsedPercent(W(0, 9), Now));

    [Theory]
    [InlineData(38, 55, BarState.Normal)]
    [InlineData(66, 55, BarState.Ahead)]
    [InlineData(60, 55, BarState.Normal)]    // exactly 5 ahead is not Ahead
    [InlineData(60.1, 55, BarState.Ahead)]
    [InlineData(82, 60, BarState.Low)]       // Low wins over Ahead
    [InlineData(80, 90, BarState.Normal)]    // exactly 20% left is not Low
    [InlineData(80.1, 90, BarState.Low)]
    public void State(double used, double elapsed, BarState expected) =>
        Assert.Equal(expected, PaceCalculator.StateFor(used, elapsed));
}
