using ClaudeTracker.Models;
using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class UsageStatusCalculatorTests
{
    [Theory]
    [InlineData(0, false, UsageStatusLevel.Safe)]
    [InlineData(25, false, UsageStatusLevel.Safe)]
    [InlineData(49, false, UsageStatusLevel.Safe)]
    [InlineData(50, false, UsageStatusLevel.Moderate)]
    [InlineData(79, false, UsageStatusLevel.Moderate)]
    [InlineData(80, false, UsageStatusLevel.Critical)]
    [InlineData(100, false, UsageStatusLevel.Critical)]
    public void CalculateStatus_UsedMode_ReturnsCorrectLevel(
        double percentage, bool showRemaining, UsageStatusLevel expected)
    {
        var result = UsageStatusCalculator.CalculateStatus(percentage, showRemaining);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, true, UsageStatusLevel.Safe)]       // 100% remaining
    [InlineData(70, true, UsageStatusLevel.Safe)]       // 30% remaining
    [InlineData(80, true, UsageStatusLevel.Safe)]       // 20% remaining
    [InlineData(85, true, UsageStatusLevel.Moderate)]   // 15% remaining
    [InlineData(90, true, UsageStatusLevel.Moderate)]   // 10% remaining
    [InlineData(95, true, UsageStatusLevel.Critical)]   // 5% remaining
    [InlineData(100, true, UsageStatusLevel.Critical)]  // 0% remaining
    public void CalculateStatus_RemainingMode_ReturnsCorrectLevel(
        double percentage, bool showRemaining, UsageStatusLevel expected)
    {
        var result = UsageStatusCalculator.CalculateStatus(percentage, showRemaining);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisplayPercentage_UsedMode_ReturnsSame()
    {
        Assert.Equal(60, UsageStatusCalculator.GetDisplayPercentage(60, false));
    }

    [Fact]
    public void GetDisplayPercentage_RemainingMode_ReturnsInverse()
    {
        Assert.Equal(40, UsageStatusCalculator.GetDisplayPercentage(60, true));
    }

    [Fact]
    public void GetDisplayPercentage_RemainingMode_ClampsToZero()
    {
        Assert.Equal(0, UsageStatusCalculator.GetDisplayPercentage(110, true));
    }
}
