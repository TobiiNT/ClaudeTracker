using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class TimeFormatTests
{
    [Fact]
    public void FormatResetTimeAbsolute_Today_12Hour()
    {
        var today = DateTime.Now.Date.AddHours(15).AddMinutes(30);
        var result = FormatterHelper.FormatResetTimeAbsolute(today.ToUniversalTime(), use24Hour: false);
        Assert.StartsWith("Today", result);
        Assert.Contains("PM", result);
    }

    [Fact]
    public void FormatResetTimeAbsolute_Today_24Hour()
    {
        var today = DateTime.Now.Date.AddHours(15).AddMinutes(30);
        var result = FormatterHelper.FormatResetTimeAbsolute(today.ToUniversalTime(), use24Hour: true);
        Assert.StartsWith("Today", result);
        Assert.Contains("15:", result);
    }

    [Fact]
    public void FormatResetTimeCombined_ShowsBoth()
    {
        var future = DateTime.UtcNow.AddHours(2);
        var result = FormatterHelper.FormatResetTimeCombined(future, use24Hour: false);
        Assert.Contains("(", result); // has parenthesized absolute time
    }

    [Fact]
    public void IsSystem24Hour_ReturnsBoolean()
    {
        // Just verify it runs without error and returns a valid boolean
        var result = FormatterHelper.IsSystem24Hour();
        Assert.IsType<bool>(result);
    }
}
