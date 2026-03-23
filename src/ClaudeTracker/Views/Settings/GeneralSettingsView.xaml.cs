using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
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
        RefreshSlider.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
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

        // Time display combo
        TimeDisplayCombo.ItemsSource = new[] {
            new { Display = "Remaining Time", Value = Models.PopoverTimeDisplay.RemainingTime },
            new { Display = "Reset Time", Value = Models.PopoverTimeDisplay.ResetTime },
            new { Display = "Both", Value = Models.PopoverTimeDisplay.Both }
        };
        TimeDisplayCombo.DisplayMemberPath = "Display";
        TimeDisplayCombo.SelectedValuePath = "Value";
        TimeDisplayCombo.SelectedValue = _vm.PopoverTimeDisplay;
        TimeDisplayCombo.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
        TimeDisplayCombo.SelectionChanged += (_, _) =>
        {
            if (TimeDisplayCombo.SelectedValue is Models.PopoverTimeDisplay v) _vm.PopoverTimeDisplay = v;
        };

        // Time format combo
        TimeFormatCombo.ItemsSource = new[] {
            new { Display = "System Default", Value = Models.TimeFormatPreference.System },
            new { Display = "12-Hour (3:59 PM)", Value = Models.TimeFormatPreference.TwelveHour },
            new { Display = "24-Hour (15:59)", Value = Models.TimeFormatPreference.TwentyFourHour }
        };
        TimeFormatCombo.DisplayMemberPath = "Display";
        TimeFormatCombo.SelectedValuePath = "Value";
        TimeFormatCombo.SelectedValue = _vm.TimeFormatPreference;
        TimeFormatCombo.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
        TimeFormatCombo.SelectionChanged += (_, _) =>
        {
            if (TimeFormatCombo.SelectedValue is Models.TimeFormatPreference v) _vm.TimeFormatPreference = v;
        };

        // Sound toggle
        SoundToggle.IsChecked = _vm.SoundEnabled;
        SoundToggle.Checked += (_, _) => { _vm.SoundEnabled = true; SoundPickerPanel.Visibility = Visibility.Visible; };
        SoundToggle.Unchecked += (_, _) => { _vm.SoundEnabled = false; SoundPickerPanel.Visibility = Visibility.Collapsed; };
        SoundPickerPanel.Visibility = _vm.SoundEnabled ? Visibility.Visible : Visibility.Collapsed;

        SoundCombo.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
        SoundCombo.ItemsSource = new[] { "Default", "Hand", "Beep", "None" };
        SoundCombo.SelectedItem = _vm.SoundName;
        SoundCombo.SelectionChanged += (_, _) =>
        {
            if (SoundCombo.SelectedItem is string v) _vm.SoundName = v;
        };

        TestAlertButton.Click += (_, _) =>
        {
            // Play the currently selected sound from the dropdown (not the saved profile sound)
            if (_vm.SoundEnabled)
            {
                var sound = _vm.SoundName switch
                {
                    "Hand" => System.Media.SystemSounds.Hand,
                    "Beep" => System.Media.SystemSounds.Beep,
                    "None" => (System.Media.SystemSound?)null,
                    _ => System.Media.SystemSounds.Exclamation
                };
                sound?.Play();
            }

            // Show popup directly to avoid double-sound from SendNotification
            var popup = new NotificationPopup(
                "ClaudeTracker - Test Alert",
                "This is a sample notification. Alerts will appear like this when usage thresholds are reached.");
            popup.Show();
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
