using System.Windows.Media;
using ClaudeTracker.Models;

namespace ClaudeTracker.Utilities;

public static class BrushHelper
{
    public static SolidColorBrush FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        return new SolidColorBrush(ThemeColors.Get("StatusSafe"));
    }

    public static SolidColorBrush GetStatusBrush(UsageStatusLevel status)
    {
        return status switch
        {
            UsageStatusLevel.Safe => new SolidColorBrush(ThemeColors.Get("StatusSafe")),
            UsageStatusLevel.Moderate => new SolidColorBrush(ThemeColors.Get("StatusModerate")),
            UsageStatusLevel.Critical => new SolidColorBrush(ThemeColors.Get("StatusCritical")),
            _ => new SolidColorBrush(ThemeColors.Get("StatusSafe"))
        };
    }
}
