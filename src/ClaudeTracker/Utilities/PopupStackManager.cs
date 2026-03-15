using System.Windows;

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

    public static void RepositionAll()
    {
        var workArea = SystemParameters.WorkArea;
        lock (_lock)
        {
            double bottomOffset = ScreenMargin;
            for (int i = _openPopups.Count - 1; i >= 0; i--)
            {
                var popup = _openPopups[i];
                popup.UpdateLayout();
                popup.BeginAnimation(Window.TopProperty, null);
                popup.BeginAnimation(Window.LeftProperty, null);
                popup.Left = workArea.Right - popup.ActualWidth - ScreenMargin;
                popup.Top = workArea.Bottom - popup.ActualHeight - bottomOffset;
                bottomOffset += popup.ActualHeight + PopupGap;
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
