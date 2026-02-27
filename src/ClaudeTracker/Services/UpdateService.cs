using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using Velopack;
using Velopack.Sources;

namespace ClaudeTracker.Services;

/// <summary>Checks for updates from GitHub Releases via Velopack.</summary>
public class UpdateService : IUpdateService, IDisposable
{
    private readonly UpdateManager? _updateManager;
    private readonly INotificationService _notificationService;
    private readonly System.Timers.Timer _periodicTimer;
    private UpdateInfo? _updateInfo;

    private const double CheckIntervalMs = 4 * 60 * 60 * 1000; // 4 hours

    public bool IsInstalled { get; }
    public bool IsUpdateAvailable { get; private set; }
    public string? AvailableVersion { get; private set; }
    public string StatusText { get; private set; } = "Idle";
    public bool IsBusy { get; private set; }

    public event EventHandler? StateChanged;

    public UpdateService(INotificationService notificationService)
    {
        _notificationService = notificationService;

        try
        {
            var source = new GithubSource(Constants.GitHub.RepoUrl, null, false);
            _updateManager = new UpdateManager(source);
            IsInstalled = _updateManager.IsInstalled;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Log($"UpdateManager init skipped (dev build): {ex.Message}");
            IsInstalled = false;
            StatusText = "Updates not available (development build)";
        }

        _periodicTimer = new System.Timers.Timer(CheckIntervalMs) { AutoReset = true };
        _periodicTimer.Elapsed += async (_, _) => await CheckForUpdatesAsync();
    }

    public async Task StartAsync()
    {
        if (!IsInstalled) return;
        _periodicTimer.Start();
        await CheckForUpdatesAsync();
    }

    public async Task CheckForUpdatesAsync()
    {
        if (!IsInstalled || _updateManager == null) return;

        try
        {
            IsBusy = true;
            StatusText = "Checking for updates...";
            RaiseStateChanged();

            _updateInfo = await _updateManager.CheckForUpdatesAsync();

            if (_updateInfo != null)
            {
                AvailableVersion = _updateInfo.TargetFullRelease.Version.ToString();
                IsUpdateAvailable = true;
                StatusText = $"Update available: v{AvailableVersion}";

                _notificationService.SendNotification(
                    "Update Available",
                    $"Claude Tracker v{AvailableVersion} is available. Open Settings > About to update.");
            }
            else
            {
                IsUpdateAvailable = false;
                AvailableVersion = null;
                StatusText = "Up to date";
            }

            LoggingService.Instance.Log($"Update check: {StatusText}");
        }
        catch (Exception ex)
        {
            StatusText = "Update check failed";
            LoggingService.Instance.LogError("Update check failed", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseStateChanged();
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (!IsInstalled || _updateManager == null || _updateInfo == null) return;

        try
        {
            IsBusy = true;
            StatusText = "Downloading update...";
            RaiseStateChanged();

            await _updateManager.DownloadUpdatesAsync(_updateInfo);

            StatusText = "Restarting...";
            RaiseStateChanged();

            _updateManager.ApplyUpdatesAndRestart(_updateInfo.TargetFullRelease);
        }
        catch (Exception ex)
        {
            StatusText = "Update failed";
            LoggingService.Instance.LogError("Update apply failed", ex);
            IsBusy = false;
            RaiseStateChanged();
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _periodicTimer.Stop();
        _periodicTimer.Dispose();
    }
}
