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
        return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    }

    public static SolidColorBrush GetStatusBrush(UsageStatusLevel status)
    {
        return status switch
        {
            UsageStatusLevel.Safe => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            UsageStatusLevel.Moderate => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
            UsageStatusLevel.Critical => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _ => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
        };
    }
}
