using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class GeneralSettingsView : UserControl
{
    private readonly GeneralSettingsViewModel _vm;

    public GeneralSettingsView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<GeneralSettingsViewModel>();
        DataContext = _vm;

        // Refresh interval
        RefreshSlider.Value = _vm.RefreshInterval;
        RefreshValueText.Text = $"{_vm.RefreshInterval:F0}s";
        RefreshSlider.ValueChanged += (_, e) =>
        {
            _vm.RefreshInterval = e.NewValue;
            RefreshValueText.Text = $"{e.NewValue:F0}s";
        };

        // Toggles
        LaunchToggle.IsChecked = _vm.LaunchAtLoginEnabled;
        LaunchToggle.Checked += (_, _) => _vm.LaunchAtLoginEnabled = true;
        LaunchToggle.Unchecked += (_, _) => _vm.LaunchAtLoginEnabled = false;

        AutoStartToggle.IsChecked = _vm.AutoStartSession;
        AutoStartToggle.Checked += (_, _) => _vm.AutoStartSession = true;
        AutoStartToggle.Unchecked += (_, _) => _vm.AutoStartSession = false;

        OverageToggle.IsChecked = _vm.CheckOverageLimit;
        OverageToggle.Checked += (_, _) => _vm.CheckOverageLimit = true;
        OverageToggle.Unchecked += (_, _) => _vm.CheckOverageLimit = false;

        NotifyToggle.IsChecked = _vm.NotificationsEnabled;
        NotifyToggle.Checked += (_, _) => { _vm.NotificationsEnabled = true; UpdateThresholdVisibility(); };
        NotifyToggle.Unchecked += (_, _) => { _vm.NotificationsEnabled = false; UpdateThresholdVisibility(); };

        Threshold75Toggle.IsChecked = _vm.Threshold75;
        Threshold75Toggle.Checked += (_, _) => _vm.Threshold75 = true;
        Threshold75Toggle.Unchecked += (_, _) => _vm.Threshold75 = false;

        Threshold90Toggle.IsChecked = _vm.Threshold90;
        Threshold90Toggle.Checked += (_, _) => _vm.Threshold90 = true;
        Threshold90Toggle.Unchecked += (_, _) => _vm.Threshold90 = false;

        Threshold95Toggle.IsChecked = _vm.Threshold95;
        Threshold95Toggle.Checked += (_, _) => _vm.Threshold95 = true;
        Threshold95Toggle.Unchecked += (_, _) => _vm.Threshold95 = false;

        TestAlertButton.Click += (_, _) =>
        {
            var notificationService = App.Services.GetRequiredService<INotificationService>();
            notificationService.SendNotification(
                "ClaudeTracker - Test Alert",
                "This is a sample notification. Alerts will appear like this when usage thresholds are reached.");
        };

        // Save button
        SaveButton.Click += (_, _) =>
        {
            _vm.SaveCommand.Execute(null);
            SaveButton.Visibility = Visibility.Collapsed;
        };
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_vm.HasUnsavedChanges))
                Dispatcher.Invoke(() => SaveButton.Visibility = _vm.HasUnsavedChanges
                    ? Visibility.Visible : Visibility.Collapsed);
        };

        UpdateThresholdVisibility();
    }

    private void UpdateThresholdVisibility()
    {
        ThresholdPanel.Visibility = _vm.NotificationsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
