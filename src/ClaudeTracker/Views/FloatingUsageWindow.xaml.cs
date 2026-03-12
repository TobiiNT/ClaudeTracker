using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views;

public partial class FloatingUsageWindow : Window
{
    private readonly PopoverViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _saveTimer;
    private bool _isDocked;
    private bool _isDragging;
    private Point _dragOffset;

    public FloatingUsageWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<PopoverViewModel>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        DataContext = _viewModel;

        CloseButton.Click += (_, _) => OnCloseRequested();
        SwitchToPopoverButton.Click += (_, _) => SwitchToPopoverRequested?.Invoke(this, EventArgs.Empty);
        DockButton.Click += (_, _) => ToggleDocked();

        // Safety nets: cancel drag if focus is lost or window deactivates
        Deactivated += (_, _) => CancelDrag();
        DragHandle.LostMouseCapture += (_, _) => CancelDrag();

        _viewModel.PropertyChanged += (_, _) => UpdateUI();
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

        _isDragging = true;
        _dragOffset = e.GetPosition(this);
        DragHandle.CaptureMouse();
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        // If mouse button was released without us noticing, cancel
        if (e.LeftButton == MouseButtonState.Released)
        {
            CancelDrag();
            return;
        }

        var screenPos = DragHandle.PointToScreen(e.GetPosition(DragHandle));
        Left = screenPos.X - _dragOffset.X;
        Top = screenPos.Y - _dragOffset.Y;
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelDrag();
        e.Handled = true;
    }

    private void CancelDrag([System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (!_isDragging) return;
        LoggingService.Instance.Log($"FloatingWidget: CancelDrag from {caller}, IsMouseCaptured={DragHandle.IsMouseCaptured}");
        _isDragging = false;
        DragHandle.ReleaseMouseCapture();
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

            WeeklyPercentText.Text = _viewModel.WeeklyPercentageText;
            WeeklyResetText.Text = _viewModel.WeeklyResetText;
            WeeklyProgressFill.Background = GetStatusBrush(_viewModel.WeeklyStatus);

            LastUpdatedText.Text = _viewModel.LastUpdatedText;

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

    private static SolidColorBrush GetStatusBrush(UsageStatusLevel status)
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
