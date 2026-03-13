using ClaudeTracker.Models;
using Xunit;

namespace ClaudeTracker.Tests;

public class EffectiveSessionTests
{
    [Fact]
    public void EffectiveSessionPercentage_ReturnsZero_WhenSessionExpired()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 75.0,
            SessionResetTime = DateTime.UtcNow.AddMinutes(-10)
        };
        Assert.Equal(0.0, usage.EffectiveSessionPercentage);
    }

    [Fact]
    public void EffectiveSessionPercentage_ReturnsRaw_WhenSessionActive()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 42.5,
            SessionResetTime = DateTime.UtcNow.AddHours(3)
        };
        Assert.Equal(42.5, usage.EffectiveSessionPercentage);
    }

    [Fact]
    public void RemainingPercentage_UsesEffective()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 80.0,
            SessionResetTime = DateTime.UtcNow.AddMinutes(-1)
        };
        Assert.Equal(100.0, usage.RemainingPercentage);
    }

    [Fact]
    public void RemainingPercentage_ClampsToZero()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 110.0,
            SessionResetTime = DateTime.UtcNow.AddHours(1)
        };
        Assert.Equal(0.0, usage.RemainingPercentage);
    }
}
