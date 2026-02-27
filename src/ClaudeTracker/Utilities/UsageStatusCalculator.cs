using ClaudeTracker.Models;

namespace ClaudeTracker.Utilities;

/// <summary>Determines usage severity level and display percentage from raw usage data.</summary>
public static class UsageStatusCalculator
{
    /// <summary>Returns Safe/Moderate/Critical based on used percentage and display mode.</summary>
    public static UsageStatusLevel CalculateStatus(double usedPercentage, bool showRemaining)
    {
        if (showRemaining)
        {
            var remainingPercentage = Math.Max(0, 100 - usedPercentage);
            return remainingPercentage switch
            {
                >= 20 => UsageStatusLevel.Safe,
                >= 10 => UsageStatusLevel.Moderate,
                _ => UsageStatusLevel.Critical
            };
        }
        else
        {
            return usedPercentage switch
            {
                < 50 => UsageStatusLevel.Safe,
                < 80 => UsageStatusLevel.Moderate,
                _ => UsageStatusLevel.Critical
            };
        }
    }

    /// <summary>Returns the percentage to display, inverted if showing remaining.</summary>
    public static double GetDisplayPercentage(double usedPercentage, bool showRemaining)
    {
        return showRemaining ? Math.Max(0, 100 - usedPercentage) : usedPercentage;
    }
}
