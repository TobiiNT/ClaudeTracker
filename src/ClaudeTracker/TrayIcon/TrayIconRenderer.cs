using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using ClaudeTracker.Models;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.TrayIcon;

/// <summary>Renders DPI-aware tray icons using SkiaSharp in multiple visual styles.</summary>
public class TrayIconRenderer
{
    private const int BaseSize = 16;

    public System.Drawing.Icon RenderIcon(
        double percentage,
        UsageStatusLevel status,
        MenuBarIconStyle style,
        bool monochrome = false,
        bool isDarkMode = false,
        string? customColorHex = null)
    {
        var dpiScale = GetDpiScale();
        var size = (int)(BaseSize * dpiScale);

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var foreground = GetForegroundColor(isDarkMode);
        var statusColor = customColorHex != null
            ? ParseHexColor(customColorHex)
            : GetStatusColor(status, monochrome, isDarkMode);

        switch (style)
        {
            case MenuBarIconStyle.Battery:
                DrawBattery(canvas, size, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.ProgressBar:
                DrawProgressBar(canvas, size, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Percentage:
                DrawPercentage(canvas, size, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Ring:
                DrawRing(canvas, size, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Compact:
                DrawCompact(canvas, size, percentage, statusColor, foreground);
                break;
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new System.IO.MemoryStream(data.ToArray());
        using var bitmap = new System.Drawing.Bitmap(stream);

        var hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private void DrawBattery(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor)
    {
        var padding = size * 0.1f;
        var capWidth = size * 0.08f;
        var bodyWidth = size - padding * 2 - capWidth;
        var bodyHeight = size - padding * 2;
        var cornerRadius = size * 0.12f;

        // Battery body outline
        using var outlinePaint = new SKPaint
        {
            Color = outlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, size * 0.06f),
            IsAntialias = true
        };
        var bodyRect = new SKRect(padding, padding, padding + bodyWidth, padding + bodyHeight);
        canvas.DrawRoundRect(bodyRect, cornerRadius, cornerRadius, outlinePaint);

        // Battery cap
        var capHeight = bodyHeight * 0.35f;
        var capY = padding + (bodyHeight - capHeight) / 2;
        var capRect = new SKRect(padding + bodyWidth, capY, padding + bodyWidth + capWidth, capY + capHeight);
        canvas.DrawRoundRect(capRect, cornerRadius * 0.5f, cornerRadius * 0.5f, outlinePaint);

        // Fill
        var fillPadding = Math.Max(1, size * 0.08f);
        var fillWidth = (bodyWidth - fillPadding * 2) * (float)(percentage / 100.0);
        if (fillWidth > 0)
        {
            using var fillPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            var fillRect = new SKRect(
                padding + fillPadding,
                padding + fillPadding,
                padding + fillPadding + fillWidth,
                padding + bodyHeight - fillPadding);
            canvas.DrawRoundRect(fillRect, cornerRadius * 0.5f, cornerRadius * 0.5f, fillPaint);
        }
    }

    private void DrawProgressBar(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor)
    {
        var padding = size * 0.15f;
        var barHeight = size * 0.35f;
        var y = (size - barHeight) / 2;
        var cornerRadius = barHeight / 2;

        // Background
        using var bgPaint = new SKPaint
        {
            Color = outlineColor.WithAlpha(60),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRect(padding, y, size - padding, y + barHeight), cornerRadius, cornerRadius, bgPaint);

        // Fill
        var fillWidth = (size - padding * 2) * (float)(percentage / 100.0);
        if (fillWidth > 0)
        {
            using var fillPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(new SKRect(padding, y, padding + fillWidth, y + barHeight), cornerRadius, cornerRadius, fillPaint);
        }
    }

    private void DrawPercentage(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor)
    {
        var text = $"{(int)Math.Round(percentage)}";
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, size * 0.65f);
        using var paint = new SKPaint
        {
            Color = fillColor,
            IsAntialias = true
        };

        var textBounds = new SKRect();
        font.MeasureText(text, out textBounds);
        var y = size / 2f - textBounds.MidY;
        canvas.DrawText(text, size / 2f, y, SKTextAlign.Center, font, paint);
    }

    private void DrawRing(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor)
    {
        var center = size / 2f;
        var radius = size * 0.38f;
        var strokeWidth = Math.Max(2, size * 0.15f);

        // Background ring
        using var bgPaint = new SKPaint
        {
            Color = outlineColor.WithAlpha(50),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawCircle(center, center, radius, bgPaint);

        // Progress arc
        if (percentage > 0)
        {
            using var progressPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            var sweepAngle = (float)(percentage / 100.0 * 360);
            var rect = new SKRect(center - radius, center - radius, center + radius, center + radius);
            using var path = new SKPath();
            path.AddArc(rect, -90, sweepAngle);
            canvas.DrawPath(path, progressPaint);
        }
    }

    private void DrawCompact(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor)
    {
        var center = size / 2f;
        var radius = size * 0.35f;

        using var paint = new SKPaint
        {
            Color = fillColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(center, center, radius, paint);
    }

    public BitmapSource RenderPreviewImage(
        double percentage,
        UsageStatusLevel status,
        MenuBarIconStyle style,
        bool monochrome = false,
        bool isDarkMode = false,
        int previewSize = 48,
        string? customColorHex = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(previewSize, previewSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var foreground = GetForegroundColor(isDarkMode);
        var statusColor = customColorHex != null
            ? ParseHexColor(customColorHex)
            : GetStatusColor(status, monochrome, isDarkMode);

        switch (style)
        {
            case MenuBarIconStyle.Battery:
                DrawBattery(canvas, previewSize, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.ProgressBar:
                DrawProgressBar(canvas, previewSize, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Percentage:
                DrawPercentage(canvas, previewSize, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Ring:
                DrawRing(canvas, previewSize, percentage, statusColor, foreground);
                break;
            case MenuBarIconStyle.Compact:
                DrawCompact(canvas, previewSize, percentage, statusColor, foreground);
                break;
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new System.IO.MemoryStream(data.ToArray());

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static SKColor ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return new SKColor(r, g, b);
        }
        return new SKColor(0x4C, 0xAF, 0x50); // fallback green
    }

    private static SKColor GetStatusColor(UsageStatusLevel status, bool monochrome, bool isDarkMode)
    {
        if (monochrome)
            return isDarkMode ? SKColors.White : SKColors.Black;

        return status switch
        {
            UsageStatusLevel.Safe => new SKColor(0x4C, 0xAF, 0x50),      // Green
            UsageStatusLevel.Moderate => new SKColor(0xFF, 0x98, 0x00),   // Orange
            UsageStatusLevel.Critical => new SKColor(0xF4, 0x43, 0x36),   // Red
            _ => new SKColor(0x4C, 0xAF, 0x50)
        };
    }

    private static SKColor GetForegroundColor(bool isDarkMode)
    {
        return isDarkMode ? SKColors.White : new SKColor(0x33, 0x33, 0x33);
    }

    private static float GetDpiScale()
    {
        try
        {
            // Can't use PresentationSource.FromVisual — tray app has no MainWindow.
            // Use Win32 GetDpiForSystem() (available on Windows 10 1607+).
            var dpi = GetDpiForSystem();
            return dpi / 96f;
        }
        catch { }
        return 1.0f;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
