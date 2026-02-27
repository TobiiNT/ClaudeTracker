namespace ClaudeTracker.Utilities;

/// <summary>Human-friendly formatting helpers for time, percentages, tokens, and burn rate.</summary>
public static class FormatterHelper
{
    /// <summary>Formats time until reset as a compact string (e.g., "2h 30m").</summary>
    public static string FormatTimeRemaining(DateTime resetTime)
    {
        var remaining = resetTime - DateTime.UtcNow;

        if (remaining.TotalSeconds <= 0)
            return "Resetting...";

        if (remaining.TotalDays >= 1)
        {
            var days = (int)remaining.TotalDays;
            var hours = remaining.Hours;
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }

        if (remaining.TotalHours >= 1)
        {
            var hours = (int)remaining.TotalHours;
            var minutes = remaining.Minutes;
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"{(int)remaining.TotalMinutes}m";
        }

        return $"{(int)remaining.TotalSeconds}s";
    }

    /// <summary>Formats an absolute reset time as a friendly date string.</summary>
    public static string FormatResetTime(DateTime resetTime)
    {
        var local = resetTime.ToLocalTime();
        var now = DateTime.Now;

        if (local.Date == now.Date)
            return $"Today at {local:h:mm tt}";
        if (local.Date == now.Date.AddDays(1))
            return $"Tomorrow at {local:h:mm tt}";

        return local.ToString("ddd, MMM d 'at' h:mm tt");
    }

    /// <summary>Formats a percentage, showing one decimal for values under 1%.</summary>
    public static string FormatPercentage(double percentage)
    {
        return percentage < 1 && percentage > 0
            ? $"{percentage:F1}%"
            : $"{(int)Math.Round(percentage)}%";
    }

    /// <summary>Formats a UTC timestamp as a relative "X ago" string.</summary>
    public static string FormatTimeAgo(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;

        if (elapsed.TotalSeconds < 10)
            return "just now";
        if (elapsed.TotalMinutes < 1)
            return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1)
            return $"{(int)elapsed.TotalHours}h ago";

        return utcTime.ToLocalTime().ToString("h:mm tt");
    }

    /// <summary>Formats a token count with K/M suffixes (e.g., "500K", "1.0M").</summary>
    public static string FormatTokenCount(int tokens)
    {
        return tokens switch
        {
            >= 1_000_000 => $"{tokens / 1_000_000.0:F1}M",
            >= 1_000 => $"{tokens / 1_000.0:F0}K",
            _ => tokens.ToString()
        };
    }

}
