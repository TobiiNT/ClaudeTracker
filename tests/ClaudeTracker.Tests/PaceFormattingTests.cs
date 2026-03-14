using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class PaceFormattingTests
{
    // --- WillExceedBeforeReset ---

    [Fact]
    public void WillExceed_EtaBeforeReset_ReturnsTrue()
    {
        var eta = TimeSpan.FromHours(2);
        var resetTime = DateTime.UtcNow.AddHours(3);
        Assert.True(PaceStatusCalculator.WillExceedBeforeReset(eta, resetTime));
    }

    [Fact]
    public void WillExceed_EtaAfterReset_ReturnsFalse()
    {
        var eta = TimeSpan.FromHours(5);
        var resetTime = DateTime.UtcNow.AddHours(3);
        Assert.False(PaceStatusCalculator.WillExceedBeforeReset(eta, resetTime));
    }

    [Fact]
    public void WillExceed_NullEta_ReturnsFalse()
    {
        Assert.False(PaceStatusCalculator.WillExceedBeforeReset(null, DateTime.UtcNow.AddHours(3)));
    }

    [Fact]
    public void WillExceed_JustBeforeReset_ReturnsTrue()
    {
        var eta = TimeSpan.FromHours(2);
        var resetTime = DateTime.UtcNow.AddHours(2.1); // ETA just before reset
        Assert.True(PaceStatusCalculator.WillExceedBeforeReset(eta, resetTime));
    }

    // --- EstimateTimeToLimit edge cases ---

    [Fact]
    public void Estimate_TooEarlyInWindow_ReturnsNull()
    {
        var resetTime = DateTime.UtcNow.AddHours(4.9); // 2% elapsed
        var eta = PaceStatusCalculator.EstimateTimeToLimit(50.0, 0.02, resetTime);
        Assert.Null(eta);
    }

    [Fact]
    public void Estimate_WindowOver_ReturnsNull()
    {
        var resetTime = DateTime.UtcNow.AddMinutes(-1);
        var eta = PaceStatusCalculator.EstimateTimeToLimit(50.0, 1.0, resetTime);
        Assert.Null(eta);
    }

    [Fact]
    public void Estimate_At100Percent_ReturnsZero()
    {
        var resetTime = DateTime.UtcNow.AddHours(2.5);
        var eta = PaceStatusCalculator.EstimateTimeToLimit(100.0, 0.5, resetTime);
        Assert.NotNull(eta);
        Assert.Equal(TimeSpan.Zero, eta);
    }
}
