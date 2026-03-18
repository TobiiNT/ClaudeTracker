using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using ClaudeTracker.Views;
using Hardcodet.Wpf.TaskbarNotification;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeTracker.TrayIcon;

/// <summary>Manages the system tray icon, popover window, tooltip, and context menu.</summary>
public class TrayIconManager : IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly TrayIconRenderer _renderer;
    private TaskbarIcon? _trayIcon;
    private PopoverWindow? _popoverWindow;
    private FloatingUsageWindow? _floatingWindow;

    // Custom tooltip (bypasses Hardcodet's broken DPI-scaled tooltip positioning)
    private Window? _tooltipWindow;
    private TextBlock? _tooltipTextBlock;
    private string _tooltipText = "Claude Tracker - Claude Usage Monitor";
    private POINT _tooltipAnchor;
    private readonly DispatcherTimer _tooltipPollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public TrayIconManager(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ISettingsService settingsService,
        INotificationService notificationService,
        TrayIconRenderer renderer)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _renderer = renderer;
        _notificationService.NotificationClicked += (_, _) =>
            Application.Current.Dispatcher.Invoke(() => ShowPopover());
        _tooltipPollTimer.Tick += (_, _) =>
        {
            // Hide when context menu is open or cursor moves away from the icon area
            if (_trayIcon?.ContextMenu is { IsOpen: true })
            {
                HideTooltip();
                return;
            }
            GetCursorPos(out var now);
            var dx = Math.Abs(now.X - _tooltipAnchor.X);
            var dy = Math.Abs(now.Y - _tooltipAnchor.Y);
            if (dx > 40 || dy > 40)
                HideTooltip();
        };
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

        // Restore floating widget if it was enabled
        if (_settingsService.Settings.IsFloatingModeEnabled)
            ShowFloatingWindow();

        LoggingService.Instance.Log("Tray icon initialized");
    }

    public void UpdateIcon()
    {
        if (_trayIcon == null) return;

        try
        {
            var profile = _profileService.ActiveProfile;
            var claudeUsage = profile?.HasClaudeAI == true ? profile.ClaudeUsage : null;
            var apiUsage = profile?.HasAPIConsole == true ? profile.ApiUsage : null;
            var iconConfig = profile?.IconConfig ?? MenuBarIconConfiguration.Default;
            var sessionConfig = iconConfig.GetConfig(MenuBarMetricType.Session);

            double percentage;
            string tooltipText;

            if (claudeUsage != null)
            {
                // Subscription user: show session usage %
                percentage = claudeUsage.EffectiveSessionPercentage;
                tooltipText = $"Claude Usage: {FormatterHelper.FormatPercentage(percentage)} used\nResets: {FormatterHelper.FormatTimeRemaining(claudeUsage.SessionResetTime)}";
            }
            else if (apiUsage != null)
            {
                // API-only user: show budget usage %
                percentage = apiUsage.UsagePercentage;
                tooltipText = $"API Budget: {apiUsage.FormattedUsed} / {apiUsage.FormattedTotal} ({FormatterHelper.FormatPercentage(percentage)} used)";
            }
            else
            {
                percentage = 0;
                tooltipText = "Claude Tracker - No usage data";
            }

            var displayPercentage = UsageStatusCalculator.GetDisplayPercentage(
                percentage, iconConfig.ShowRemainingPercentage);
            var status = UsageStatusCalculator.CalculateStatus(
                percentage, iconConfig.ShowRemainingPercentage);
            var style = sessionConfig?.IconStyle ?? MenuBarIconStyle.Battery;
            var isDark = App.IsSystemDarkMode();

            var customColor = iconConfig.UseCustomColor ? iconConfig.CustomColorHex : null;
            var metricPrefix = iconConfig.ShowIconNames
                ? (claudeUsage != null ? "S:" : "$:")
                : null;
            var icon = _renderer.RenderIcon(
                displayPercentage, status, style,
                iconConfig.MonochromeMode, isDark, customColor,
                iconConfig.ShowIconNames, metricPrefix);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var oldIcon = _trayIcon.Icon;
                _trayIcon.Icon = icon;
                oldIcon?.Dispose();

                _tooltipText = tooltipText;
            });
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to update tray icon", ex);
        }
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e) => TogglePopover();

    public void TogglePopover(bool fromHotkey = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_popoverWindow != null && _popoverWindow.IsVisible)
            {
                _popoverWindow.Hide();
                return;
            }

            ShowPopover(fromHotkey);
        });
    }

    private void ShowPopover(bool fromHotkey = false)
    {
        if (_popoverWindow == null)
        {
            _popoverWindow = new PopoverWindow();
            _popoverWindow.Deactivated += (_, _) =>
            {
                _popoverWindow.Hide();
            };
            _popoverWindow.SwitchToWidgetRequested += (_, _) =>
            {
                ToggleFloatingMode(true);
                UpdateFloatingMenuCheckmark(true);
            };
        }

        // Position near tray icon — show first so layout/DPI are available
        _popoverWindow.Left = -9999;
        _popoverWindow.Top = -9999;
        _popoverWindow.Show();
        PositionPopover(fromHotkey);
        _popoverWindow.Activate();
    }

    private void PositionPopover(bool fromHotkey = false)
    {
        if (_popoverWindow == null || _trayIcon == null) return;

        var workArea = SystemParameters.WorkArea;
        var popoverWidth = Constants.WindowSizes.PopoverWidth;

        // Use actual rendered height since window is now shown
        _popoverWindow.UpdateLayout();
        var popoverHeight = _popoverWindow.ActualHeight;
        if (popoverHeight <= 0) popoverHeight = Constants.WindowSizes.PopoverMaxHeight;

        var taskbarEdge = GetTaskbarEdge();
        double left, top;
        const double gap = 4;

        if (fromHotkey)
        {
            // Hotkey: anchor to right side of work area, near the taskbar
            left = workArea.Right - popoverWidth - gap;
            top = taskbarEdge == TaskbarEdge.Top
                ? workArea.Top + gap
                : workArea.Bottom - popoverHeight - gap;
        }
        else
        {
            // Tray click: anchor near cursor
            GetCursorPos(out var cursorPos);
            var dpi = GetDpiForSystem();
            var dpiScaleFactor = dpi / 96.0;
            var cursorX = cursorPos.X / dpiScaleFactor;
            var cursorY = cursorPos.Y / dpiScaleFactor;

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
        var menuStyle = (Style)Application.Current.FindResource("TrayContextMenuStyle");
        var itemStyle = (Style)Application.Current.FindResource("TrayMenuItemStyle");
        var sepStyle = (Style)Application.Current.FindResource("TrayMenuSeparatorStyle");

        var menu = new ContextMenu { Style = menuStyle };

        var refreshItem = new MenuItem { Header = "Refresh Now", Style = itemStyle };
        refreshItem.Click += (_, _) => _refreshCoordinator.RefreshNow();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator { Style = sepStyle });

        var hasMetrics = _profileService.ActiveProfile?.HasUsageCredentials == true;
        var floatingItem = new MenuItem
        {
            Header = "Floating Widget",
            IsCheckable = true,
            IsChecked = _settingsService.Settings.IsFloatingModeEnabled,
            IsEnabled = hasMetrics,
            Style = itemStyle
        };
        floatingItem.Click += (_, _) => ToggleFloatingMode(floatingItem.IsChecked);
        if (hasMetrics)
            menu.Items.Add(floatingItem);

        menu.Items.Add(new Separator { Style = sepStyle });

        var settingsItem = new MenuItem { Header = "Settings...", Style = itemStyle };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator { Style = sepStyle });

        var quitItem = new MenuItem { Header = "Quit Claude Tracker", Style = itemStyle };
        quitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        return menu;
    }

    private void ShowSettings()
    {
        SettingsWindow.ShowInstance();
    }

    #region Floating Widget

    private void ToggleFloatingMode(bool enabled)
    {
        _settingsService.Settings.IsFloatingModeEnabled = enabled;
        _settingsService.Save();

        if (enabled)
            ShowFloatingWindow();
        else
            HideFloatingWindow();
    }

    private void ShowFloatingWindow()
    {
        if (_floatingWindow == null)
        {
            _floatingWindow = new FloatingUsageWindow();
            _floatingWindow.CloseRequested += (_, _) =>
            {
                ToggleFloatingMode(false);
                UpdateFloatingMenuCheckmark(false);
            };
            _floatingWindow.SwitchToPopoverRequested += (_, _) =>
            {
                ToggleFloatingMode(false);
                UpdateFloatingMenuCheckmark(false);
                ShowPopover();
            };
        }

        _floatingWindow.Show();
        _floatingWindow.RestorePosition();
    }

    private void HideFloatingWindow()
    {
        _floatingWindow?.Hide();
    }

    private void UpdateFloatingMenuCheckmark(bool isChecked)
    {
        if (_trayIcon?.ContextMenu is not { } menu) return;
        foreach (var item in menu.Items)
        {
            if (item is MenuItem { Header: "Floating Widget" } mi)
            {
                mi.IsChecked = isChecked;
                break;
            }
        }
    }

    #endregion

    #region Custom Tooltip

    private void OnTrayMouseMove(object sender, RoutedEventArgs e)
    {
        // Don't show tooltip when popover or context menu is visible
        if (_popoverWindow is { IsVisible: true })
            return;
        if (_trayIcon?.ContextMenu is { IsOpen: true })
            return;

        // Record anchor position on first show
        if (_tooltipWindow == null || !_tooltipWindow.IsVisible)
        {
            GetCursorPos(out _tooltipAnchor);
            ShowTooltip();
            _tooltipPollTimer.Start();
        }
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

        if (_tooltipTextBlock!.Text != _tooltipText)
            _tooltipTextBlock.Text = _tooltipText;

        if (!_tooltipWindow.IsVisible)
        {
            // Use cursor X as icon center (mouse is over the icon),
            // and position just above the taskbar edge.
            var dpi = GetDpiForSystem();
            var scale = dpi / 96.0;
            var iconCenterX = _tooltipAnchor.X / scale;

            // Measure tooltip width
            _tooltipWindow.Left = -9999;
            _tooltipWindow.Top = -9999;
            _tooltipWindow.Show();
            _tooltipWindow.UpdateLayout();
            var tipWidth = _tooltipWindow.ActualWidth;
            var tipHeight = _tooltipWindow.ActualHeight;

            var workArea = SystemParameters.WorkArea;
            var taskbarEdge = GetTaskbarEdge();
            double left, top;
            const double gap = 8;

            // Center horizontally on the icon
            left = iconCenterX - tipWidth / 2;

            // Position on the inner side of the taskbar
            top = taskbarEdge switch
            {
                TaskbarEdge.Top => workArea.Top + gap,
                TaskbarEdge.Left => _tooltipAnchor.Y / scale,
                TaskbarEdge.Right => _tooltipAnchor.Y / scale,
                _ => workArea.Bottom - tipHeight - gap // Bottom (most common)
            };

            // Clamp within screen
            left = Math.Clamp(left, workArea.Left + gap, workArea.Right - tipWidth - gap);

            _tooltipWindow.Left = left;
            _tooltipWindow.Top = top;
        }
    }

    private void HideTooltip()
    {
        _tooltipPollTimer.Stop();
        _tooltipWindow?.Hide();
    }

    #endregion

    public void Dispose()
    {
        _tooltipPollTimer.Stop();
        _tooltipWindow?.Close();
        _floatingWindow?.Close();
        _popoverWindow?.Close();
        _trayIcon?.Dispose();
    }
}
