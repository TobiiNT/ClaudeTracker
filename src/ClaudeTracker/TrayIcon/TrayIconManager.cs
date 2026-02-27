using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using ClaudeTracker.Views;

namespace ClaudeTracker.TrayIcon;

public class TrayIconManager : IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly ISettingsService _settingsService;
    private readonly TrayIconRenderer _renderer;
    private TaskbarIcon? _trayIcon;
    private PopoverWindow? _popoverWindow;

    // Custom tooltip (bypasses Hardcodet's broken DPI-scaled tooltip positioning)
    private Window? _tooltipWindow;
    private TextBlock? _tooltipTextBlock;
    private string _tooltipText = "ClaudeTracker - Claude Usage Monitor";
    private readonly DispatcherTimer _tooltipHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public TrayIconManager(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ISettingsService settingsService,
        TrayIconRenderer renderer)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;
        _settingsService = settingsService;
        _renderer = renderer;
        _tooltipHideTimer.Tick += (_, _) => HideTooltip();
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            // Leave ToolTipText empty to disable Hardcodet's broken WPF tooltip.
            // We manage our own tooltip window via TrayMouseMove.
            ToolTipText = "",
            ContextMenu = CreateContextMenu(),
            MenuActivation = PopupActivationMode.RightClick
        };

        _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
        _trayIcon.TrayMouseMove += OnTrayMouseMove;
        _trayIcon.TrayLeftMouseDown += (_, _) => HideTooltip();
        _trayIcon.TrayRightMouseDown += (_, _) => HideTooltip();

        _profileService.ActiveProfileChanged += (_, _) => UpdateIcon();
        _refreshCoordinator.RefreshCompleted += (_, _) => UpdateIcon();

        UpdateIcon();

        LoggingService.Instance.Log("Tray icon initialized");
    }

    public void UpdateIcon()
    {
        if (_trayIcon == null) return;

        try
        {
            var profile = _profileService.ActiveProfile;
            var usage = profile?.ClaudeUsage;
            var iconConfig = profile?.IconConfig ?? MenuBarIconConfiguration.Default;
            var sessionConfig = iconConfig.GetConfig(MenuBarMetricType.Session);

            var percentage = usage?.SessionPercentage ?? 0;
            var displayPercentage = UsageStatusCalculator.GetDisplayPercentage(
                percentage, iconConfig.ShowRemainingPercentage);
            var status = UsageStatusCalculator.CalculateStatus(
                percentage, iconConfig.ShowRemainingPercentage);
            var style = sessionConfig?.IconStyle ?? MenuBarIconStyle.Battery;
            var isDark = App.IsSystemDarkMode();

            var customColor = iconConfig.UseCustomColor ? iconConfig.CustomColorHex : null;
            var icon = _renderer.RenderIcon(
                displayPercentage, status, style,
                iconConfig.MonochromeMode, isDark, customColor);

            Application.Current.Dispatcher.Invoke(() =>
            {
                _trayIcon.Icon = icon;

                _tooltipText = usage != null
                    ? $"Claude Usage: {FormatterHelper.FormatPercentage(percentage)} used\nResets: {FormatterHelper.FormatTimeRemaining(usage.SessionResetTime)}"
                    : "ClaudeTracker - No usage data";
            });
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to update tray icon", ex);
        }
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e)
    {
        if (_popoverWindow != null && _popoverWindow.IsVisible)
        {
            _popoverWindow.Hide();
            return;
        }

        ShowPopover();
    }

    private void ShowPopover()
    {
        if (_popoverWindow == null)
        {
            _popoverWindow = new PopoverWindow();
            _popoverWindow.Deactivated += (_, _) =>
            {
                _popoverWindow.Hide();
            };
        }

        // Position near tray icon — show first so layout/DPI are available
        _popoverWindow.Left = -9999;
        _popoverWindow.Top = -9999;
        _popoverWindow.Show();
        PositionPopover();
        _popoverWindow.Activate();
    }

    private void PositionPopover()
    {
        if (_popoverWindow == null || _trayIcon == null) return;

        // Get cursor position in physical pixels
        GetCursorPos(out var cursorPos);

        // Get DPI scale factor (physical pixels per WPF unit)
        var dpi = GetDpiForSystem();
        var dpiScaleFactor = dpi / 96.0;

        // Convert cursor from physical pixels to WPF logical units
        var cursorX = cursorPos.X / dpiScaleFactor;
        var cursorY = cursorPos.Y / dpiScaleFactor;

        var workArea = SystemParameters.WorkArea;
        var popoverWidth = Constants.WindowSizes.PopoverWidth;

        // Use actual rendered height since window is now shown
        _popoverWindow.UpdateLayout();
        var popoverHeight = _popoverWindow.ActualHeight;
        if (popoverHeight <= 0) popoverHeight = Constants.WindowSizes.PopoverMaxHeight;

        var taskbarEdge = GetTaskbarEdge();
        double left, top;
        const double gap = 8;

        switch (taskbarEdge)
        {
            case TaskbarEdge.Top:
                left = cursorX - popoverWidth / 2;
                top = workArea.Top + gap;
                break;

            case TaskbarEdge.Left:
                left = workArea.Left + gap;
                top = cursorY - popoverHeight / 2;
                break;

            case TaskbarEdge.Right:
                left = workArea.Right - popoverWidth - gap;
                top = cursorY - popoverHeight / 2;
                break;

            default: // Bottom (most common)
                left = cursorX - popoverWidth / 2;
                top = workArea.Bottom - popoverHeight - gap;
                break;
        }

        // Clamp to work area
        left = Math.Clamp(left, workArea.Left + gap, workArea.Right - popoverWidth - gap);
        top = Math.Clamp(top, workArea.Top + gap, workArea.Bottom - popoverHeight - gap);

        _popoverWindow.Left = left;
        _popoverWindow.Top = top;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private enum TaskbarEdge { Bottom, Top, Left, Right }

    private static TaskbarEdge GetTaskbarEdge()
    {
        var workArea = SystemParameters.WorkArea;
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;

        if (workArea.Bottom < screenH) return TaskbarEdge.Bottom;
        if (workArea.Top > 0) return TaskbarEdge.Top;
        if (workArea.Right < screenW) return TaskbarEdge.Right;
        if (workArea.Left > 0) return TaskbarEdge.Left;
        return TaskbarEdge.Bottom;
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var refreshItem = new MenuItem { Header = "Refresh Now" };
        refreshItem.Click += (_, _) => _refreshCoordinator.RefreshNow();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit ClaudeTracker" };
        quitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        return menu;
    }

    private void ShowSettings()
    {
        SettingsWindow.ShowInstance();
    }

    #region Custom Tooltip

    private void OnTrayMouseMove(object sender, RoutedEventArgs e)
    {
        // Don't show tooltip when popover is visible
        if (_popoverWindow is { IsVisible: true })
            return;

        _tooltipHideTimer.Stop();
        _tooltipHideTimer.Start();
        ShowTooltip();
    }

    private void ShowTooltip()
    {
        if (_tooltipWindow == null)
        {
            _tooltipTextBlock = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap
            };
            _tooltipWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                IsHitTestVisible = false,
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Child = _tooltipTextBlock,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 6,
                        ShadowDepth = 2,
                        Opacity = 0.3,
                        Color = Colors.Black
                    }
                }
            };
        }

        _tooltipTextBlock!.Text = _tooltipText;

        // Position near cursor with DPI correction
        GetCursorPos(out var pt);
        var dpi = GetDpiForSystem();
        var scale = dpi / 96.0;
        _tooltipWindow.Left = pt.X / scale + 12;
        _tooltipWindow.Top = pt.Y / scale - 40;

        if (!_tooltipWindow.IsVisible)
            _tooltipWindow.Show();
    }

    private void HideTooltip()
    {
        _tooltipHideTimer.Stop();
        _tooltipWindow?.Hide();
    }

    #endregion

    public void Dispose()
    {
        _tooltipHideTimer.Stop();
        _tooltipWindow?.Close();
        _popoverWindow?.Close();
        _trayIcon?.Dispose();
    }
}
