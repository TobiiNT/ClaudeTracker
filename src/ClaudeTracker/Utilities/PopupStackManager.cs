using System.Windows;
using ClaudeTracker.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeTracker.Utilities;

public static class PopupStackManager
{
    private static readonly List<Window> _openPopups = new();
    private static readonly object _lock = new();
    private const double ScreenMargin = 12;
    private const double PopupGap = 8;

    public static void Register(Window popup)
    {
        lock (_lock) { _openPopups.Add(popup); }
        popup.Closed += OnPopupClosed;
    }

    public static string GetPosition()
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return settings.Settings.HookPopupPosition ?? "BottomRight";
        }
        catch { return "BottomRight"; }
    }

    /// <summary>Returns the work area for the configured monitor (WPF device-independent units).</summary>
    public static Rect GetWorkArea()
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            var monitorIndex = settings.Settings.HookPopupMonitor;

            var screens = System.Windows.Forms.Screen.AllScreens;
            if (monitorIndex >= 0 && monitorIndex < screens.Length)
            {
                var screen = screens[monitorIndex];
                var wa = screen.WorkingArea;
                // Convert from physical pixels to WPF device-independent units
                var dpi = GetDpiScale();
                return new Rect(wa.X / dpi, wa.Y / dpi, wa.Width / dpi, wa.Height / dpi);
            }
        }
        catch { /* fall through */ }

        return SystemParameters.WorkArea; // primary monitor fallback
    }

    /// <summary>Returns the list of monitor display names for settings UI.</summary>
    public static string[] GetMonitorNames()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var names = new string[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            names[i] = s.Primary ? $"Monitor {i + 1} (Primary)" : $"Monitor {i + 1}";
        }
        return names;
    }

    private static double GetDpiScale()
    {
        try
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return GetDpiScaleFromSystem();

            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
                return source.CompositionTarget.TransformToDevice.M11;
        }
        catch { /* fall through */ }
        return GetDpiScaleFromSystem();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static double GetDpiScaleFromSystem()
    {
        try { return GetDpiForSystem() / 96.0; }
        catch { return 1.0; }
    }

    public static void PositionWindow(Window popup)
    {
        var workArea = GetWorkArea();
        var pos = GetPosition();

        popup.Left = pos.Contains("Left")
            ? workArea.Left + ScreenMargin
            : workArea.Right - popup.ActualWidth - ScreenMargin;

        popup.Top = pos.Contains("Bottom")
            ? workArea.Bottom - popup.ActualHeight - ScreenMargin
            : workArea.Top + ScreenMargin;
    }

    public static void RepositionAll()
    {
        var workArea = GetWorkArea();
        var pos = GetPosition();
        var isBottom = pos.Contains("Bottom");
        var isLeft = pos.Contains("Left");

        lock (_lock)
        {
            double offset = ScreenMargin;
            var start = isBottom ? _openPopups.Count - 1 : 0;
            var end = isBottom ? -1 : _openPopups.Count;
            var step = isBottom ? -1 : 1;

            for (int i = start; i != end; i += step)
            {
                var popup = _openPopups[i];
                popup.UpdateLayout();
                popup.BeginAnimation(Window.TopProperty, null);
                popup.BeginAnimation(Window.LeftProperty, null);

                popup.Left = isLeft
                    ? workArea.Left + ScreenMargin
                    : workArea.Right - popup.ActualWidth - ScreenMargin;

                popup.Top = isBottom
                    ? workArea.Bottom - popup.ActualHeight - offset
                    : workArea.Top + offset;

                offset += popup.ActualHeight + PopupGap;
            }
        }
    }

    private static void OnPopupClosed(object? sender, EventArgs e)
    {
        if (sender is not Window popup) return;
        popup.Closed -= OnPopupClosed;
        lock (_lock) { _openPopups.Remove(popup); }
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(RepositionAll);
    }
}
