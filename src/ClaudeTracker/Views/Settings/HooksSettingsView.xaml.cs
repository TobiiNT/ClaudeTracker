using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class HooksSettingsView : UserControl
{
    private readonly HooksSettingsViewModel _vm;

    public HooksSettingsView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<HooksSettingsViewModel>();
        DataContext = _vm;

        // Enable hooks
        HooksEnabledToggle.IsChecked = _vm.HooksEnabled;
        UpdateHooksEnabledVisibility(_vm.HooksEnabled);
        HooksEnabledToggle.Checked += (_, _) => { _vm.HooksEnabled = true; UpdateHooksEnabledVisibility(true); };
        HooksEnabledToggle.Unchecked += (_, _) => { _vm.HooksEnabled = false; UpdateHooksEnabledVisibility(false); };

        // Popups
        PermissionPopupsToggle.IsChecked = _vm.PermissionPopupsEnabled;
        PermissionPopupsToggle.Checked += (_, _) => _vm.PermissionPopupsEnabled = true;
        PermissionPopupsToggle.Unchecked += (_, _) => _vm.PermissionPopupsEnabled = false;

        ElicitationPopupsToggle.IsChecked = _vm.ElicitationPopupsEnabled;
        ElicitationPopupsToggle.Checked += (_, _) => _vm.ElicitationPopupsEnabled = true;
        ElicitationPopupsToggle.Unchecked += (_, _) => _vm.ElicitationPopupsEnabled = false;

        // Popup position
        PopupPositionCombo.ItemsSource = new[] { "Top Right", "Top Left", "Bottom Right", "Bottom Left" };
        PopupPositionCombo.SelectedItem = FormatPosition(_vm.PopupPosition);
        PopupPositionCombo.SelectionChanged += (_, _) =>
        {
            if (PopupPositionCombo.SelectedItem is string v)
                _vm.PopupPosition = v.Replace(" ", "");
        };

        // Notifications
        NotifyStopToggle.IsChecked = _vm.NotifyStop;
        NotifyStopToggle.Checked += (_, _) => _vm.NotifyStop = true;
        NotifyStopToggle.Unchecked += (_, _) => _vm.NotifyStop = false;

        NotifyToolErrorToggle.IsChecked = _vm.NotifyToolError;
        NotifyToolErrorToggle.Checked += (_, _) => _vm.NotifyToolError = true;
        NotifyToolErrorToggle.Unchecked += (_, _) => _vm.NotifyToolError = false;

        NotifyPermissionToggle.IsChecked = _vm.NotifyPermission;
        NotifyPermissionToggle.Checked += (_, _) => _vm.NotifyPermission = true;
        NotifyPermissionToggle.Unchecked += (_, _) => _vm.NotifyPermission = false;

        NotifyIdleToggle.IsChecked = _vm.NotifyIdle;
        NotifyIdleToggle.Checked += (_, _) => _vm.NotifyIdle = true;
        NotifyIdleToggle.Unchecked += (_, _) => _vm.NotifyIdle = false;

        NotifyConfigChangeToggle.IsChecked = _vm.NotifyConfigChange;
        NotifyConfigChangeToggle.Checked += (_, _) => _vm.NotifyConfigChange = true;
        NotifyConfigChangeToggle.Unchecked += (_, _) => _vm.NotifyConfigChange = false;

        NotifySessionLifecycleToggle.IsChecked = _vm.NotifySessionLifecycle;
        NotifySessionLifecycleToggle.Checked += (_, _) => _vm.NotifySessionLifecycle = true;
        NotifySessionLifecycleToggle.Unchecked += (_, _) => _vm.NotifySessionLifecycle = false;

        NotifySubagentToggle.IsChecked = _vm.NotifySubagent;
        NotifySubagentToggle.Checked += (_, _) => _vm.NotifySubagent = true;
        NotifySubagentToggle.Unchecked += (_, _) => _vm.NotifySubagent = false;

        // Activity feed
        ActivityFeedToggle.IsChecked = _vm.ActivityFeedEnabled;
        MaxFeedPanel.Visibility = _vm.ActivityFeedEnabled ? Visibility.Visible : Visibility.Collapsed;
        ActivityFeedToggle.Checked += (_, _) => { _vm.ActivityFeedEnabled = true; MaxFeedPanel.Visibility = Visibility.Visible; };
        ActivityFeedToggle.Unchecked += (_, _) => { _vm.ActivityFeedEnabled = false; MaxFeedPanel.Visibility = Visibility.Collapsed; };

        // Clear activity feed
        ClearFeedButton.Click += (_, _) =>
        {
            var activityService = App.Services.GetRequiredService<IActivityService>();
            activityService.Clear();
        };

        // Max feed entries slider
        MaxFeedSlider.Value = _vm.MaxFeedEntries;
        MaxFeedValueText.Text = $"{_vm.MaxFeedEntries}";
        MaxFeedSlider.ValueChanged += (_, e) =>
        {
            _vm.MaxFeedEntries = (int)e.NewValue;
            MaxFeedValueText.Text = $"{(int)e.NewValue}";
        };

        // Install / Uninstall buttons
        InstallButton.Click += async (_, _) => { await RunBridgeCommandAsync("install"); _vm.CheckInstallStatus(); UpdateInstallUI(); };
        UninstallButton.Click += async (_, _) => { await RunBridgeCommandAsync("uninstall"); _vm.CheckInstallStatus(); UpdateInstallUI(); };

        // Check installation status on load
        _vm.CheckInstallStatus();
        UpdateInstallUI();

        // Save button (auto-uninstall when disabling hooks)
        SaveButton.Click += async (_, _) =>
        {
            var wasEnabled = _vm.IsHooksInstalled;
            _vm.SaveCommand.Execute(null);
            SaveButton.Visibility = Visibility.Collapsed;

            // Auto-uninstall hooks when user disables and saves
            if (wasEnabled && !_vm.HooksEnabled)
            {
                await RunBridgeCommandAsync("uninstall");
                _vm.CheckInstallStatus();
                UpdateInstallUI();
            }
        };
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_vm.HasUnsavedChanges))
                Dispatcher.Invoke(() => SaveButton.Visibility = _vm.HasUnsavedChanges
                    ? Visibility.Visible : Visibility.Collapsed);
        };
    }

    private static string FormatPosition(string pos) => pos switch
    {
        "TopLeft" => "Top Left",
        "BottomRight" => "Bottom Right",
        "BottomLeft" => "Bottom Left",
        _ => "Bottom Right"
    };

    private void UpdateHooksEnabledVisibility(bool enabled)
    {
        var vis = enabled ? Visibility.Visible : Visibility.Collapsed;
        InstallationPanel.Visibility = vis;
        HooksSettingsPanel.Visibility = vis;
    }

    private void UpdateInstallUI()
    {
        InstallStatusText.Text = _vm.InstallStatusText;
        InstallButton.IsEnabled = !_vm.IsHooksInstalled;
        UninstallButton.IsEnabled = _vm.IsHooksInstalled;
    }

    private static string? FindHookBridge()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ClaudeTracker.HookBridge.exe"),
            Path.Combine(AppContext.BaseDirectory, "HookBridge", "ClaudeTracker.HookBridge.exe"),
            Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\', '/')) ?? "", "ClaudeTracker.HookBridge", "ClaudeTracker.HookBridge.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task RunBridgeCommandAsync(string command)
    {
        var bridgePath = FindHookBridge();
        if (bridgePath == null)
        {
            InstallStatusText.Text = "HookBridge not found. Build the ClaudeTracker.HookBridge project first, or place it alongside ClaudeTracker.exe.";
            return;
        }

        InstallButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        InstallStatusText.Text = $"Running {command}...";

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                InstallStatusText.Text = process.ExitCode == 0
                    ? $"Success: {output.Trim()}"
                    : $"Error: {(string.IsNullOrEmpty(error) ? output : error).Trim()}";
            }
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = $"Failed to run HookBridge: {ex.Message}";
        }
    }
}
