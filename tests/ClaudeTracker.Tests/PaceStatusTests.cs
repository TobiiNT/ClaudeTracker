using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class PaceStatusTests
{
    [Fact]
    public void Calculate_ReturnsNull_WhenElapsedTooLow()
    {
        Assert.Null(PaceStatusCalculator.Calculate(50.0, 0.02));
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenPeriodOver()
    {
        Assert.Null(PaceStatusCalculator.Calculate(50.0, 1.0));
    }

    [Fact]
    public void Calculate_ReturnsComfortable_WhenZeroUsage()
    {
        Assert.Equal(PaceStatus.Comfortable, PaceStatusCalculator.Calculate(0.0, 0.5));
    }

    [Theory]
    [InlineData(10.0, 0.5, PaceStatus.Comfortable)]    // projected 0.2
    [InlineData(30.0, 0.5, PaceStatus.OnTrack)]          // projected 0.6
    [InlineData(40.0, 0.5, PaceStatus.Warming)]           // projected 0.8
    [InlineData(47.0, 0.5, PaceStatus.Pressing)]          // projected 0.94
    [InlineData(55.0, 0.5, PaceStatus.Critical)]           // projected 1.1
    [InlineData(70.0, 0.5, PaceStatus.Runaway)]            // projected 1.4
    public void Calculate_ReturnsTier_BasedOnProjection(double used, double elapsed, PaceStatus expected)
    {
        Assert.Equal(expected, PaceStatusCalculator.Calculate(used, elapsed));
    }

    [Fact]
    public void CalculateSessionElapsed_CorrectlyComputes()
    {
        var resetTime = DateTime.UtcNow.AddHours(2.5); // 2.5h remaining of 5h window
        var elapsed = PaceStatusCalculator.CalculateSessionElapsed(resetTime);
        Assert.InRange(elapsed, 0.49, 0.51); // ~50% elapsed
    }

    [Fact]
    public void CalculateWeeklyElapsed_CorrectlyComputes()
    {
        var resetTime = DateTime.UtcNow.AddDays(3.5); // 3.5 days remaining of 7 day window
        var elapsed = PaceStatusCalculator.CalculateWeeklyElapsed(resetTime);
        Assert.InRange(elapsed, 0.49, 0.51); // ~50% elapsed
    }

    [Fact]
    public void GetColorHex_ReturnsCorrectHex()
    {
        Assert.Equal("#4CAF50", PaceStatusCalculator.GetColorHex(PaceStatus.Comfortable));
        Assert.Equal("#009688", PaceStatusCalculator.GetColorHex(PaceStatus.OnTrack));
        Assert.Equal("#FFC107", PaceStatusCalculator.GetColorHex(PaceStatus.Warming));
        Assert.Equal("#FF9800", PaceStatusCalculator.GetColorHex(PaceStatus.Pressing));
        Assert.Equal("#F44336", PaceStatusCalculator.GetColorHex(PaceStatus.Critical));
        Assert.Equal("#9C27B0", PaceStatusCalculator.GetColorHex(PaceStatus.Runaway));
    }
}
