using System.Diagnostics;
using System.IO;
using System.Windows;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeTracker.Views;

public partial class HooksOnboardingWindow : Window
{
    public HooksOnboardingWindow()
    {
        InitializeComponent();

        SkipButton.Click += (_, _) =>
        {
            MarkSeen();
            Close();
        };

        SetupButton.Click += async (_, _) =>
        {
            SetupButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            StatusText.Text = "Installing hooks...";
            StatusText.Visibility = Visibility.Visible;
            ProgressBar.Visibility = Visibility.Visible;

            var success = await InstallHooksAsync();

            if (success)
            {
                // Enable hooks in settings and start IPC
                var settingsService = App.Services.GetRequiredService<ISettingsService>();
                settingsService.Settings.HooksEnabled = true;
                settingsService.Save();

                var dispatcher = App.Services.GetRequiredService<IHookEventDispatcher>();
                dispatcher.Initialize();

                var ipcService = App.Services.GetRequiredService<IHookIpcService>();
                ipcService.Start();

                MarkSeen();
                Close();
            }
            else
            {
                // Show error, let user retry or skip
                ProgressBar.Visibility = Visibility.Collapsed;
                SetupButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
            }
        };
    }

    private static void MarkSeen()
    {
        var settingsService = App.Services.GetRequiredService<ISettingsService>();
        settingsService.Settings.HooksOnboardingSeen = true;
        settingsService.Save();
    }

    private async Task<bool> InstallHooksAsync()
    {
        var bridgePath = FindHookBridge();
        if (bridgePath == null)
        {
            StatusText.Text = "HookBridge not found. Please reinstall ClaudeTracker.";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return false;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = "install",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process == null)
            {
                StatusText.Text = "Failed to start HookBridge.";
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                LoggingService.Instance.Log($"[HooksOnboarding] Install succeeded: {output.Trim()}");
                return true;
            }

            StatusText.Text = $"Install failed: {(string.IsNullOrEmpty(error) ? output : error).Trim()}";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return false;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return false;
        }
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
}
