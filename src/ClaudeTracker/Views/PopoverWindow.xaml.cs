using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views;

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

        _viewModel.PropertyChanged += (_, _) => UpdateUI();
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

            NoCredentialsPanel.Visibility = _viewModel.HasCredentials ? Visibility.Collapsed : Visibility.Visible;
            SessionCard.Visibility = _viewModel.HasClaudeUsage ? Visibility.Visible : Visibility.Collapsed;
            WeeklyCard.Visibility = _viewModel.HasClaudeUsage ? Visibility.Visible : Visibility.Collapsed;

            // Session
            SessionPercentText.Text = _viewModel.SessionPercentageText;
            SessionResetText.Text = _viewModel.SessionResetText;
            SessionProgressFill.Background = GetStatusBrush(_viewModel.SessionStatus);

            // Weekly
            WeeklyPercentText.Text = _viewModel.WeeklyPercentageText;
            WeeklyResetText.Text = _viewModel.WeeklyResetText;
            WeeklyProgressFill.Background = GetStatusBrush(_viewModel.WeeklyStatus);

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

            // Status
            StatusDot.Fill = GetStatusBrush(_viewModel.SessionStatus);
            StatusText.Text = _viewModel.IsRefreshing ? "Refreshing..." : _viewModel.SessionResetText;

            LastUpdatedText.Text = _viewModel.LastUpdatedText;

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
