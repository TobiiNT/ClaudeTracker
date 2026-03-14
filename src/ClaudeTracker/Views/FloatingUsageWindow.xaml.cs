using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views;

public partial class FloatingUsageWindow : Window
{
    private readonly PopoverViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _saveTimer;
    private bool _isDocked;

    public FloatingUsageWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<PopoverViewModel>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        DataContext = _viewModel;

        CloseButton.Click += (_, _) => OnCloseRequested();
        SwitchToPopoverButton.Click += (_, _) => SwitchToPopoverRequested?.Invoke(this, EventArgs.Empty);
        DockButton.Click += (_, _) => ToggleDocked();

        var uiDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        uiDebounce.Tick += (_, _) => { uiDebounce.Stop(); UpdateUI(); };
        _viewModel.PropertyChanged += (_, _) => { uiDebounce.Stop(); uiDebounce.Start(); };
        SizeChanged += (_, _) => UpdateProgressBars();
        LocationChanged += OnLocationChanged;

        // Debounced position save — 500ms after last move
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePosition();
        };

        // Restore docked state
        _isDocked = _settingsService.Settings.IsFloatingWidgetDocked;
        UpdateDockVisual();

        UpdateUI();
    }

    /// <summary>Raised when the user clicks the close button.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user clicks the switch-to-popover button.</summary>
    public event EventHandler? SwitchToPopoverRequested;

    public void RestorePosition()
    {
        var settings = _settingsService.Settings;
        var workArea = SystemParameters.WorkArea;

        if (settings.FloatingWindowLeft.HasValue && settings.FloatingWindowTop.HasValue)
        {
            // Clamp to current work area bounds
            Left = Math.Clamp(settings.FloatingWindowLeft.Value,
                workArea.Left, workArea.Right - ActualWidth);
            Top = Math.Clamp(settings.FloatingWindowTop.Value,
                workArea.Top, workArea.Bottom - ActualHeight);
        }
        else
        {
            // Default: bottom-right corner with margin
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - 200;
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDocked) return;
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ToggleDocked()
    {
        _isDocked = !_isDocked;
        _settingsService.Settings.IsFloatingWidgetDocked = _isDocked;
        _settingsService.Save();
        UpdateDockVisual();
    }

    private void UpdateDockVisual()
    {
        DockIcon.Kind = _isDocked
            ? MaterialDesignThemes.Wpf.PackIconKind.Pin
            : MaterialDesignThemes.Wpf.PackIconKind.PinOutline;
        DockButton.ToolTip = _isDocked ? "Undock" : "Dock";
        DragHandle.Cursor = _isDocked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        // Restart debounce timer on each move
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePosition()
    {
        _settingsService.Settings.FloatingWindowLeft = Left;
        _settingsService.Settings.FloatingWindowTop = Top;
        _settingsService.Save();
    }

    private void OnCloseRequested()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateUI()
    {
        Dispatcher.Invoke(() =>
        {
            SessionPercentText.Text = _viewModel.SessionPercentageText;
            SessionResetText.Text = _viewModel.SessionResetText;
            SessionProgressFill.Background = GetStatusBrush(_viewModel.SessionStatus);
            UpdatePacePanel(SessionPacePanel, SessionPaceDot, SessionPaceText, SessionEstimateText,
                _viewModel.SessionPaceLabel, _viewModel.SessionPaceColorHex,
                _viewModel.SessionEstimateText, _viewModel.SessionPaceTooltip);

            WeeklyPercentText.Text = _viewModel.WeeklyPercentageText;
            WeeklyResetText.Text = _viewModel.WeeklyResetText;
            WeeklyProgressFill.Background = GetStatusBrush(_viewModel.WeeklyStatus);
            UpdatePacePanel(WeeklyPacePanel, WeeklyPaceDot, WeeklyPaceText, WeeklyEstimateText,
                _viewModel.WeeklyPaceLabel, _viewModel.WeeklyPaceColorHex,
                _viewModel.WeeklyEstimateText, _viewModel.WeeklyPaceTooltip);

            // Status line
            if (_viewModel.IsRefreshing)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                LastUpdatedText.Text = "Refreshing...";
            }
            else if (!_viewModel.HasCredentials)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
                LastUpdatedText.Text = "No account connected";
            }
            else if (!_viewModel.HasClaudeUsage)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                LastUpdatedText.Text = "Waiting for data...";
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // green = connected
                LastUpdatedText.Text = _viewModel.LastUpdatedText;
            }

            UpdateProgressBars();
        });
    }

    private void UpdateProgressBars()
    {
        SetProgressWidth(SessionProgressFill, _viewModel.SessionPercentage);
        SetProgressWidth(WeeklyProgressFill, _viewModel.WeeklyPercentage);
    }

    private static void SetProgressWidth(FrameworkElement fill, double percentage)
    {
        if (fill.Parent is FrameworkElement parent && parent.ActualWidth > 0)
        {
            fill.Width = parent.ActualWidth * Math.Clamp(percentage / 100.0, 0, 1);
        }
    }

    private static void UpdatePacePanel(
        System.Windows.Controls.StackPanel panel,
        System.Windows.Shapes.Ellipse dot,
        System.Windows.Controls.TextBlock paceText,
        System.Windows.Controls.TextBlock estimateText,
        string label, string colorHex, string estimate, string tooltip)
    {
        if (!string.IsNullOrEmpty(label))
        {
            panel.Visibility = Visibility.Visible;
            dot.Fill = BrushFromHex(colorHex);
            paceText.Text = label;
            paceText.Foreground = BrushFromHex(colorHex);
            estimateText.Text = estimate;
            panel.ToolTip = tooltip;
        }
        else
        {
            panel.Visibility = Visibility.Collapsed;
        }
    }

    private static SolidColorBrush BrushFromHex(string hex) => Utilities.BrushHelper.FromHex(hex);
    private static SolidColorBrush GetStatusBrush(UsageStatusLevel status) => Utilities.BrushHelper.GetStatusBrush(status);
}
