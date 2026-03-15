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
            return settings.Settings.HookPopupPosition ?? "TopRight";
        }
        catch { return "BottomRight"; }
    }

    public static void PositionWindow(Window popup)
    {
        var workArea = SystemParameters.WorkArea;
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
        var workArea = SystemParameters.WorkArea;
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
