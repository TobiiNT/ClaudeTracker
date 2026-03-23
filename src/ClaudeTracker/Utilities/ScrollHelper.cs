using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeTracker.Utilities;

/// <summary>
/// Routes mouse wheel events from nested controls (Slider, ComboBox) to the
/// parent ScrollViewer so the page scrolls instead of the control changing value.
/// </summary>
public static class ScrollHelper
{
    /// <summary>
    /// PreviewMouseWheel handler that intercepts the wheel event on a nested control
    /// and re-raises it on the nearest parent ScrollViewer.
    /// Usage: slider.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
    /// </summary>
    public static void RouteMouseWheelToParent(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var scrollViewer = FindParent<ScrollViewer>((DependencyObject)sender);
        scrollViewer?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent
        });
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T target) return target;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
