using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views;

public class ActivityIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ActivityIcon icon ? icon switch
        {
            ActivityIcon.Tool => "\u26A1",         // ⚡
            ActivityIcon.Permission => "\uD83D\uDD10", // 🔐
            ActivityIcon.Session => "\uD83D\uDCBB",    // 💻
            ActivityIcon.Subagent => "\u25B6\uFE0F",   // ▶️
            ActivityIcon.Notification => "\uD83D\uDD14", // 🔔
            ActivityIcon.System => "\u2699\uFE0F",     // ⚙️
            _ => "\u26A1"
        } : "\u26A1";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class PopoverWindow : Window
{
    private readonly PopoverViewModel _viewModel;
    private bool _suppressSelectionChanged;

    public event EventHandler? SwitchToWidgetRequested;

    public PopoverWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<PopoverViewModel>();
        DataContext = _viewModel;

        RefreshButton.Click += (_, _) => _viewModel.RefreshCommand.Execute(null);
        SwitchToWidgetButton.Click += (_, _) =>
        {
            Hide();
            SwitchToWidgetRequested?.Invoke(this, EventArgs.Empty);
        };
        SettingsButton.Click += (_, _) =>
        {
            Hide();
            SettingsWindow.ShowInstance();
        };
        QuitButton.Click += (_, _) => Application.Current.Shutdown();

        ProfileCombo.ItemsSource = _viewModel.Profiles;
        ProfileCombo.SelectionChanged += (_, _) =>
        {
            if (!_suppressSelectionChanged && ProfileCombo.SelectedValue is Guid id)
                _viewModel.SwitchProfileCommand.Execute(id);
        };

        SessionsList.ItemsSource = _viewModel.ActiveSessions;
        ActivityFeedList.ItemsSource = _viewModel.ActivityFeed;

        // Debounce: batch rapid property changes into one UI update
        var uiDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~1 frame at 60fps
        };
        uiDebounce.Tick += (_, _) => { uiDebounce.Stop(); UpdateUI(); };
        _viewModel.PropertyChanged += (_, _) => { uiDebounce.Stop(); uiDebounce.Start(); };

        SizeChanged += (_, e) =>
        {
            UpdateProgressBars();
            if (IsVisible && e.HeightChanged)
            {
                var workArea = SystemParameters.WorkArea;
                var bottom = Top + e.PreviousSize.Height;
                Top = bottom - ActualHeight;
                Top = Math.Clamp(Top, workArea.Top + 4, workArea.Bottom - ActualHeight - 4);
            }
        };
        UpdateUI();
    }

    private void UpdateUI()
    {
        Dispatcher.Invoke(() =>
        {
            _suppressSelectionChanged = true;
            ProfileCombo.SelectedValue =
                App.Services.GetRequiredService<Services.Interfaces.IProfileService>().ActiveProfile?.Id;
            _suppressSelectionChanged = false;

            // Claude system status
            if (_viewModel.ShowClaudeStatus)
            {
                ClaudeStatusPanel.Visibility = Visibility.Visible;
                ClaudeStatusDot.Fill = BrushFromHex(_viewModel.ClaudeStatusColorHex);
                ClaudeStatusText.Text = _viewModel.ClaudeStatusDescription;
                ClaudeStatusText.Foreground = BrushFromHex(_viewModel.ClaudeStatusColorHex);
                ClaudeStatusTooltip.Text = $"{_viewModel.ClaudeStatusDescription}\nClick to view details on status.claude.com";
            }
            else
            {
                ClaudeStatusPanel.Visibility = Visibility.Collapsed;
            }

            NoCredentialsPanel.Visibility = _viewModel.HasCredentials ? Visibility.Collapsed : Visibility.Visible;
            SessionCard.Visibility = _viewModel.HasClaudeUsage ? Visibility.Visible : Visibility.Collapsed;
            WeeklyCard.Visibility = _viewModel.HasClaudeUsage ? Visibility.Visible : Visibility.Collapsed;

            // Session
            SessionPercentText.Text = _viewModel.SessionPercentageText;
            SessionResetText.Text = _viewModel.SessionResetText;
            SessionProgressFill.Background = GetStatusBrush(_viewModel.SessionStatus);

            // Session pace
            if (!string.IsNullOrEmpty(_viewModel.SessionPaceLabel))
            {
                SessionPacePanel.Visibility = Visibility.Visible;
                SessionPaceDot.Fill = BrushFromHex(_viewModel.SessionPaceColorHex);
                SessionPaceText.Text = _viewModel.SessionPaceLabel;
                SessionPaceText.Foreground = BrushFromHex(_viewModel.SessionPaceColorHex);
                SessionEstimateText.Text = _viewModel.SessionEstimateText;
                SessionPacePanel.ToolTip = _viewModel.SessionPaceTooltip;
            }
            else
            {
                SessionPacePanel.Visibility = Visibility.Collapsed;
            }

            // Weekly
            WeeklyPercentText.Text = _viewModel.WeeklyPercentageText;
            WeeklyResetText.Text = _viewModel.WeeklyResetText;
            WeeklyProgressFill.Background = GetStatusBrush(_viewModel.WeeklyStatus);

            // Weekly pace
            if (!string.IsNullOrEmpty(_viewModel.WeeklyPaceLabel))
            {
                WeeklyPacePanel.Visibility = Visibility.Visible;
                WeeklyPaceDot.Fill = BrushFromHex(_viewModel.WeeklyPaceColorHex);
                WeeklyPaceText.Text = _viewModel.WeeklyPaceLabel;
                WeeklyPaceText.Foreground = BrushFromHex(_viewModel.WeeklyPaceColorHex);
                WeeklyEstimateText.Text = _viewModel.WeeklyEstimateText;
                WeeklyPacePanel.ToolTip = _viewModel.WeeklyPaceTooltip;
            }
            else
            {
                WeeklyPacePanel.Visibility = Visibility.Collapsed;
            }

            // Model-specific
            OpusPercentText.Text = _viewModel.OpusPercentageText;
            SonnetPercentText.Text = _viewModel.SonnetPercentageText;

            // Model-specific cards
            ModelCardsGrid.Visibility = _viewModel.HasModelData ? Visibility.Visible : Visibility.Collapsed;

            // Cost
            CostCard.Visibility = _viewModel.HasCostData ? Visibility.Visible : Visibility.Collapsed;
            CostText.Text = _viewModel.CostText;

            // API
            ApiCard.Visibility = _viewModel.HasApiUsage ? Visibility.Visible : Visibility.Collapsed;
            ApiProgress.Value = _viewModel.ApiPercentage;
            ApiUsedText.Text = $"Used: {_viewModel.ApiUsedText}";
            ApiRemainingText.Text = $"Remaining: {_viewModel.ApiRemainingText}";

            // Status line
            if (_viewModel.IsRefreshing)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // blue
                StatusText.Text = "Refreshing...";
            }
            else if (!_viewModel.HasCredentials)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)); // gray
                StatusText.Text = "No account connected";
            }
            else if (!_viewModel.HasClaudeUsage)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // orange
                StatusText.Text = "Waiting for usage data...";
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // green = connected
                StatusText.Text = _viewModel.LastUpdatedText;
            }

            LastUpdatedText.Text = _viewModel.LastUpdatedText;

            // Sessions card
            SessionsCard.Visibility = _viewModel.HasActiveSessions ? Visibility.Visible : Visibility.Collapsed;
            if (_viewModel.HasActiveSessions)
                SessionCountText.Text = _viewModel.ActiveSessionCount.ToString();

            // Activity feed
            ActivityFeedCard.Visibility = _viewModel.ShowActivityFeed && _viewModel.ActivityFeed.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
            ActivityFeedList.Visibility = _viewModel.IsActivityFeedExpanded
                ? Visibility.Visible : Visibility.Collapsed;
            ActivityChevron.Kind = _viewModel.IsActivityFeedExpanded
                ? MaterialDesignThemes.Wpf.PackIconKind.ChevronDown
                : MaterialDesignThemes.Wpf.PackIconKind.ChevronRight;
            ActivityCountText.Text = _viewModel.ActivityFeed.Count > 0
                ? $"({_viewModel.ActivityFeed.Count})" : "";

            UpdateProgressBars();
        });
    }

    private void UpdateProgressBars()
    {
        // Fill custom progress borders proportionally to their parent width
        SetProgressWidth(SessionProgressFill, _viewModel.SessionPercentage);
        SetProgressWidth(WeeklyProgressFill, _viewModel.WeeklyPercentage);
        SetProgressWidth(OpusProgressFill, _viewModel.OpusPercentage);
        SetProgressWidth(SonnetProgressFill, _viewModel.SonnetPercentage);

        SetTimeMarker(SessionTimeMarker, SessionProgressFill,
            _viewModel.SessionElapsedFraction, _viewModel.SessionPaceColorHex);
        SetTimeMarker(WeeklyTimeMarker, WeeklyProgressFill,
            _viewModel.WeeklyElapsedFraction, _viewModel.WeeklyPaceColorHex);
    }

    private static void SetProgressWidth(FrameworkElement fill, double percentage)
    {
        if (fill.Parent is FrameworkElement parent && parent.ActualWidth > 0)
        {
            fill.Width = parent.ActualWidth * Math.Clamp(percentage / 100.0, 0, 1);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private static void SetTimeMarker(FrameworkElement marker, FrameworkElement progressFill,
        double elapsedFraction, string paceColorHex)
    {
        if (elapsedFraction > 0.03 && elapsedFraction < 1.0
            && progressFill.Parent is FrameworkElement parent
            && parent.Parent is FrameworkElement grandParent
            && grandParent.ActualWidth > 0)
        {
            marker.Visibility = Visibility.Visible;
            marker.Margin = new Thickness(grandParent.ActualWidth * elapsedFraction - 1, 0, 0, 0);

            if (marker is System.Windows.Controls.Border border)
            {
                var color = BrushFromHex(paceColorHex);
                color.Opacity = 0.85;
                border.Background = color;
                var pct = (int)(elapsedFraction * 100);
                border.ToolTip = $"Time elapsed: {pct}% of window\nUsage should ideally be at or below this point";
            }
        }
        else
        {
            marker.Visibility = Visibility.Collapsed;
        }
    }

    private void ClaudeStatusPanel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://status.claude.com") { UseShellExecute = true });
        }
        catch { }
    }

    private void ActivityFeedHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.IsActivityFeedExpanded = !_viewModel.IsActivityFeedExpanded;
        ActivityFeedList.Visibility = _viewModel.IsActivityFeedExpanded
            ? Visibility.Visible : Visibility.Collapsed;
        ActivityChevron.Kind = _viewModel.IsActivityFeedExpanded
            ? MaterialDesignThemes.Wpf.PackIconKind.ChevronDown
            : MaterialDesignThemes.Wpf.PackIconKind.ChevronRight;
    }

    private static SolidColorBrush BrushFromHex(string hex) => Utilities.BrushHelper.FromHex(hex);
    private static SolidColorBrush GetStatusBrush(UsageStatusLevel status) => Utilities.BrushHelper.GetStatusBrush(status);
}
