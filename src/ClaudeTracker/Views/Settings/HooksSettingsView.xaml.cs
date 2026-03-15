using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
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
        HooksEnabledToggle.Checked += (_, _) => _vm.HooksEnabled = true;
        HooksEnabledToggle.Unchecked += (_, _) => _vm.HooksEnabled = false;

        // Popups
        PermissionPopupsToggle.IsChecked = _vm.PermissionPopupsEnabled;
        PermissionPopupsToggle.Checked += (_, _) => _vm.PermissionPopupsEnabled = true;
        PermissionPopupsToggle.Unchecked += (_, _) => _vm.PermissionPopupsEnabled = false;

        ElicitationPopupsToggle.IsChecked = _vm.ElicitationPopupsEnabled;
        ElicitationPopupsToggle.Checked += (_, _) => _vm.ElicitationPopupsEnabled = true;
        ElicitationPopupsToggle.Unchecked += (_, _) => _vm.ElicitationPopupsEnabled = false;

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
        ActivityFeedToggle.Checked += (_, _) => _vm.ActivityFeedEnabled = true;
        ActivityFeedToggle.Unchecked += (_, _) => _vm.ActivityFeedEnabled = false;

        // Max feed entries slider
        MaxFeedSlider.Value = _vm.MaxFeedEntries;
        MaxFeedValueText.Text = $"{_vm.MaxFeedEntries}";
        MaxFeedSlider.ValueChanged += (_, e) =>
        {
            _vm.MaxFeedEntries = (int)e.NewValue;
            MaxFeedValueText.Text = $"{(int)e.NewValue}";
        };

        // Install / Uninstall buttons
        InstallButton.Click += OnInstallClick;
        UninstallButton.Click += OnUninstallClick;

        // Check installation status on load
        _vm.CheckInstallStatus();
        UpdateInstallUI();

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
    }

    private void UpdateInstallUI()
    {
        InstallStatusText.Text = _vm.InstallStatusText;
        InstallButton.IsEnabled = !_vm.IsHooksInstalled;
        UninstallButton.IsEnabled = _vm.IsHooksInstalled;
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        RunBridgeCommand("install");
        _vm.CheckInstallStatus();
        UpdateInstallUI();
    }

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        RunBridgeCommand("uninstall");
        _vm.CheckInstallStatus();
        UpdateInstallUI();
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

    private void RunBridgeCommand(string command)
    {
        var bridgePath = FindHookBridge();
        if (bridgePath == null)
        {
            InstallStatusText.Text = "HookBridge not found. Build the ClaudeTracker.HookBridge project first, or place it alongside ClaudeTracker.exe.";
            return;
        }

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
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

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
