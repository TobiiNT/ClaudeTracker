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
        StatusIndicator.None     => "#4CAF50",
        StatusIndicator.Minor    => "#FFC107",
        StatusIndicator.Major    => "#FF9800",
        StatusIndicator.Critical => "#F44336",
        StatusIndicator.Unknown  => "#9E9E9E",
        _                        => "#9E9E9E"
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
