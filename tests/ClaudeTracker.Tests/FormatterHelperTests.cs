using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class FormatterHelperTests
{
    [Fact]
    public void FormatPercentage_Zero_Returns0Percent()
    {
        Assert.Equal("0%", FormatterHelper.FormatPercentage(0));
    }

    [Fact]
    public void FormatPercentage_SmallValue_ShowsDecimal()
    {
        Assert.Equal("0.5%", FormatterHelper.FormatPercentage(0.5));
    }

    [Fact]
    public void FormatPercentage_Normal_ReturnsRounded()
    {
        Assert.Equal("75%", FormatterHelper.FormatPercentage(75.3));
    }

    [Fact]
    public void FormatTokenCount_Millions_FormatsCorrectly()
    {
        Assert.Equal("1.0M", FormatterHelper.FormatTokenCount(1_000_000));
    }

    [Fact]
    public void FormatTokenCount_Thousands_FormatsCorrectly()
    {
        Assert.Equal("500K", FormatterHelper.FormatTokenCount(500_000));
    }

    [Fact]
    public void FormatTokenCount_Small_ReturnsRaw()
    {
        Assert.Equal("999", FormatterHelper.FormatTokenCount(999));
    }

    [Fact]
    public void FormatTimeRemaining_PastTime_ReturnsResetting()
    {
        var past = DateTime.UtcNow.AddMinutes(-5);
        Assert.Equal("Resetting...", FormatterHelper.FormatTimeRemaining(past));
    }

    [Fact]
    public void FormatTimeRemaining_FutureHours_IncludesHoursAndMinutes()
    {
        var future = DateTime.UtcNow.AddHours(2).AddMinutes(30);
        var result = FormatterHelper.FormatTimeRemaining(future);
        Assert.Contains("h", result);
    }

    [Fact]
    public void FormatTimeRemaining_FutureDays_IncludesDays()
    {
        var future = DateTime.UtcNow.AddDays(2);
        var result = FormatterHelper.FormatTimeRemaining(future);
        Assert.Contains("d", result);
    }
}
