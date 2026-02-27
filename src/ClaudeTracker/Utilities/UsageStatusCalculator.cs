using ClaudeTracker.Models;

namespace ClaudeTracker.Utilities;

public static class UsageStatusCalculator
{
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

    public static double GetDisplayPercentage(double usedPercentage, bool showRemaining)
    {
        return showRemaining ? Math.Max(0, 100 - usedPercentage) : usedPercentage;
    }
}
