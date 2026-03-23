using ClaudeTracker.Models;
using Xunit;

namespace ClaudeTracker.Tests;

public class ClaudeStatusTests
{
    [Fact]
    public void Unknown_HasCorrectDefaults()
    {
        var status = ClaudeStatus.Unknown;
        Assert.Equal(StatusIndicator.Unknown, status.Indicator);
        Assert.Equal("Status Unknown", status.Description);
    }

    [Fact]
    public void Operational_HasCorrectDefaults()
    {
        var status = ClaudeStatus.Operational;
        Assert.Equal(StatusIndicator.None, status.Indicator);
        Assert.Equal("All Systems Operational", status.Description);
    }

    [Theory]
    [InlineData(StatusIndicator.None, "#4CAF50")]
    [InlineData(StatusIndicator.Minor, "#FFC107")]
    [InlineData(StatusIndicator.Major, "#FF9800")]
    [InlineData(StatusIndicator.Critical, "#F44336")]
    [InlineData(StatusIndicator.Unknown, "#888888")]
    public void GetColorHex_ReturnsCorrectColor(StatusIndicator indicator, string expectedHex)
    {
        Assert.Equal(expectedHex, ClaudeStatus.GetColorHex(indicator));
    }

    [Theory]
    [InlineData("none", StatusIndicator.None)]
    [InlineData("minor", StatusIndicator.Minor)]
    [InlineData("major", StatusIndicator.Major)]
    [InlineData("critical", StatusIndicator.Critical)]
    [InlineData("banana", StatusIndicator.Unknown)]
    public void ParseIndicator_MapsCorrectly(string input, StatusIndicator expected)
    {
        Assert.Equal(expected, ClaudeStatus.ParseIndicator(input));
    }
}
