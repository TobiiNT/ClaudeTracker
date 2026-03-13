using System.Windows.Threading;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class UsageRefreshCoordinator : IUsageRefreshCoordinator, IDisposable
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly INotificationService _notificationService;
    private readonly IClaudeStatusService _statusService;
    private DispatcherTimer? _timer;
    private ClaudeStatus _cachedStatus = ClaudeStatus.Unknown;
    private DateTime _lastStatusFetch = DateTime.MinValue;

    public bool IsRunning => _timer?.IsEnabled ?? false;
    public ClaudeStatus CurrentStatus => _cachedStatus;

    public event EventHandler? RefreshStarted;
    public event EventHandler? RefreshCompleted;
    public event EventHandler<string>? RefreshFailed;

    public UsageRefreshCoordinator(
        IClaudeApiService apiService,
        IProfileService profileService,
        INotificationService notificationService,
        IClaudeStatusService statusService)
    {
        _apiService = apiService;
        _profileService = profileService;
        _notificationService = notificationService;
        _statusService = statusService;

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start()
    {
        if (_timer != null) return;

        var interval = _profileService.ActiveProfile?.RefreshInterval ?? Constants.RefreshIntervals.DefaultSeconds;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        // Initial refresh
        _ = RefreshAsync();

        LoggingService.Instance.Log($"UsageRefreshCoordinator started (interval: {interval}s)");
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        LoggingService.Instance.Log("UsageRefreshCoordinator stopped");
    }

    public void RefreshNow()
    {
        _ = RefreshAsync();
    }

    public void UpdateInterval(double seconds)
    {
        seconds = Math.Clamp(seconds, Constants.RefreshIntervals.MinSeconds, Constants.RefreshIntervals.MaxSeconds);
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            LoggingService.Instance.Log($"Refresh interval updated to {seconds}s");
        }
    }

    private async Task RefreshAsync()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null || !profile.HasUsageCredentials)
        {
            return;
        }

        RefreshStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // Fetch Claude.ai usage
            if (profile.HasClaudeAI || !string.IsNullOrEmpty(profile.CliCredentialsJSON))
            {
                var usage = await _apiService.FetchUsageData();
                _profileService.UpdateUsageData(profile.Id, claudeUsage: usage);
                _notificationService.CheckAndNotify(profile, usage);
            }

            // Fetch API Console usage
            if (profile.HasAPIConsole)
            {
                var apiUsage = await _apiService.FetchAPIUsageData(
                    profile.ApiOrganizationId!, profile.ApiSessionKey!);
                _profileService.UpdateUsageData(profile.Id, apiUsage: apiUsage);
            }

            // Fetch Claude system status (every 5 minutes)
            if ((DateTime.UtcNow - _lastStatusFetch).TotalMinutes >= Constants.StatusAPI.RefreshIntervalMinutes)
            {
                _cachedStatus = await _statusService.FetchStatusAsync();
                _lastStatusFetch = DateTime.UtcNow;
            }

            RefreshCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Usage refresh failed", ex);
            RefreshFailed?.Invoke(this, ex.Message);
        }
    }

    private async void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            LoggingService.Instance.Log("System resumed from sleep");
            await Task.Delay(2000); // Brief delay after wake
            await RefreshAsync();
        }
    }

    public void Dispose()
    {
        Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
