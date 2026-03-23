using ClaudeTracker.Utilities;

namespace ClaudeTracker.Models;

public enum StatusIndicator
{
    None,       // All systems operational
    Minor,      // Minor issues
    Major,      // Major outage
    Critical,   // Critical outage
    Unknown     // Unable to fetch
}

public record ClaudeStatus(StatusIndicator Indicator, string Description)
{
    public static ClaudeStatus Unknown => new(StatusIndicator.Unknown, "Status Unknown");
    public static ClaudeStatus Operational => new(StatusIndicator.None, "All Systems Operational");

    public static string GetColorHex(StatusIndicator indicator) => indicator switch
    {
        StatusIndicator.None     => ThemeColors.GetHex("StatusSafe"),
        StatusIndicator.Minor    => ThemeColors.GetHex("AccentAmber"),
        StatusIndicator.Major    => ThemeColors.GetHex("StatusModerate"),
        StatusIndicator.Critical => ThemeColors.GetHex("StatusCritical"),
        StatusIndicator.Unknown  => ThemeColors.GetHex("TextMuted"),
        _                        => ThemeColors.GetHex("TextMuted")
    };

    public static StatusIndicator ParseIndicator(string value) => value switch
    {
        "none"     => StatusIndicator.None,
        "minor"    => StatusIndicator.Minor,
        "major"    => StatusIndicator.Major,
        "critical" => StatusIndicator.Critical,
        _          => StatusIndicator.Unknown
    };
}
