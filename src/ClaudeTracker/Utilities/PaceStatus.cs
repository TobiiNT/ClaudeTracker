namespace ClaudeTracker.Utilities;

/// <summary>6-tier pace urgency spectrum. Projects end-of-period usage from current consumption rate.</summary>
public enum PaceStatus
{
    Comfortable = 0,  // projected <50%
    OnTrack = 1,      // projected 50-75%
    Warming = 2,      // projected 75-90%
    Pressing = 3,     // projected 90-100%
    Critical = 4,     // projected 100-120%
    Runaway = 5       // projected >120%
}

public static class PaceStatusCalculator
{
    private const double SessionWindowHours = 5.0;
    private const double WeeklyWindowDays = 7.0;

    /// <summary>
    /// Calculate pace status from current usage and elapsed time fraction.
    /// Returns null when insufficient data (&lt;3% elapsed or period over).
    /// </summary>
    public static PaceStatus? Calculate(double usedPercentage, double elapsedFraction)
    {
        if (elapsedFraction < 0.03 || elapsedFraction >= 1.0)
            return null;

        if (usedPercentage <= 0)
            return PaceStatus.Comfortable;

        var projected = (usedPercentage / 100.0) / elapsedFraction;
        return projected switch
        {
            < 0.50 => PaceStatus.Comfortable,
            < 0.75 => PaceStatus.OnTrack,
            < 0.90 => PaceStatus.Warming,
            < 1.00 => PaceStatus.Pressing,
            < 1.20 => PaceStatus.Critical,
            _      => PaceStatus.Runaway
        };
    }

    /// <summary>Calculate elapsed fraction of the 5-hour session window.</summary>
    public static double CalculateSessionElapsed(DateTime resetTime)
    {
        var windowStart = resetTime.AddHours(-SessionWindowHours);
        var totalSeconds = SessionWindowHours * 3600;
        var elapsedSeconds = (DateTime.UtcNow - windowStart).TotalSeconds;
        return Math.Clamp(elapsedSeconds / totalSeconds, 0, 1);
    }

    /// <summary>Calculate elapsed fraction of the 7-day weekly window.</summary>
    public static double CalculateWeeklyElapsed(DateTime resetTime)
    {
        var windowStart = resetTime.AddDays(-WeeklyWindowDays);
        var totalSeconds = WeeklyWindowDays * 24 * 3600;
        var elapsedSeconds = (DateTime.UtcNow - windowStart).TotalSeconds;
        return Math.Clamp(elapsedSeconds / totalSeconds, 0, 1);
    }

    /// <summary>
    /// Estimates how long until usage hits 100% at the current burn rate.
    /// Returns null if usage is 0, data is insufficient, or limit won't be reached before reset.
    /// </summary>
    /// <summary>
    /// Estimates how long until usage hits 100% at the current burn rate.
    /// Always returns a value when there's enough data (usage > 0, elapsed >= 3%).
    /// The caller can compare with the reset time to decide if it will actually be reached.
    /// </summary>
    public static TimeSpan? EstimateTimeToLimit(double usedPercentage, double elapsedFraction, DateTime resetTime)
    {
        if (elapsedFraction < 0.03 || elapsedFraction >= 1.0 || usedPercentage <= 0)
            return null;

        var remaining = resetTime - DateTime.UtcNow;
        if (remaining.TotalSeconds <= 0) return null;

        var totalWindowSec = remaining.TotalSeconds / (1.0 - elapsedFraction);
        var elapsedSec = totalWindowSec * elapsedFraction;

        var burnRate = usedPercentage / elapsedSec;
        if (burnRate <= 0) return null;

        var percentRemaining = 100.0 - usedPercentage;
        if (percentRemaining <= 0) return TimeSpan.Zero;

        var secondsToLimit = percentRemaining / burnRate;
        return TimeSpan.FromSeconds(secondsToLimit);
    }

    /// <summary>Whether the estimated time to limit exceeds the remaining window time.</summary>
    public static bool WillExceedBeforeReset(TimeSpan? eta, DateTime resetTime)
    {
        if (!eta.HasValue) return false;
        var remaining = resetTime - DateTime.UtcNow;
        return eta.Value.TotalSeconds <= remaining.TotalSeconds;
    }

    /// <summary>Returns the Material Design hex color for a pace status.</summary>
    public static string GetColorHex(PaceStatus status)
    {
        return status switch
        {
            PaceStatus.Comfortable => "#4CAF50",
            PaceStatus.OnTrack     => "#009688",
            PaceStatus.Warming     => "#FFC107",
            PaceStatus.Pressing    => "#FF9800",
            PaceStatus.Critical    => "#F44336",
            PaceStatus.Runaway     => "#9C27B0",
            _                      => "#4CAF50"
        };
    }
}
