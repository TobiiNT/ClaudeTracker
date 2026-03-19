using System.Net.Http;
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
    private readonly ClaudeCodeSyncService _cliSyncService;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _resetTimer;
    private ClaudeStatus _cachedStatus = ClaudeStatus.Unknown;
    private DateTime _lastStatusFetch = DateTime.MinValue;
    private bool _isRefreshing;
    private DateTime _rateLimitedUntil = DateTime.MinValue;
    private DispatcherTimer? _rateLimitTimer;
    private int _consecutiveRateLimits;
    private long _lastApiFetchTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan ApiRefreshInterval = TimeSpan.FromMinutes(5);

    public bool IsRunning => _timer?.IsEnabled ?? false;
    public ClaudeStatus CurrentStatus => _cachedStatus;
    public bool IsRateLimited => DateTime.UtcNow < _rateLimitedUntil;
    public DateTime RateLimitedUntil => _rateLimitedUntil;

    public event EventHandler? RefreshStarted;
    public event EventHandler? RefreshCompleted;
    public event EventHandler<string>? RefreshFailed;

    public UsageRefreshCoordinator(
        IClaudeApiService apiService,
        IProfileService profileService,
        INotificationService notificationService,
        IClaudeStatusService statusService,
        ClaudeCodeSyncService cliSyncService)
    {
        _apiService = apiService;
        _profileService = profileService;
        _notificationService = notificationService;
        _statusService = statusService;
        _cliSyncService = cliSyncService;

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
        _resetTimer?.Stop();
        _resetTimer = null;
        _rateLimitTimer?.Stop();
        _rateLimitTimer = null;
        LoggingService.Instance.Log("UsageRefreshCoordinator stopped");
    }

    public void RefreshNow()
    {
        _ = RefreshAsync();
    }

    public void InvalidateApiCache()
    {
        Interlocked.Exchange(ref _lastApiFetchTicks, DateTime.MinValue.Ticks);
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
        if (_isRefreshing) return; // Prevent overlapping requests

        // Skip if rate-limited — don't make requests that extend the limit
        if (DateTime.UtcNow < _rateLimitedUntil)
        {
            LoggingService.Instance.Log($"Skipping refresh — rate limited until {_rateLimitedUntil:HH:mm:ss}");
            var remaining = _rateLimitedUntil - DateTime.UtcNow;
            RefreshFailed?.Invoke(this, $"Rate limited — retrying in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m");
            return;
        }

        _isRefreshing = true;

        var profile = _profileService.ActiveProfile;
        if (profile == null || !profile.HasUsageCredentials)
        {
            _isRefreshing = false;
            return;
        }

        RefreshStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // Fetch Claude.ai subscription usage (Claude Session Key or Claude OAuth)
            if (profile.HasClaudeSessionKey || profile.HasClaudeOAuth)
            {
                // Silent refresh for default profile if token expired
                if (!string.IsNullOrEmpty(profile.CliCredentialsJSON) && _cliSyncService.IsTokenExpired(profile.CliCredentialsJSON))
                {
                    var isDefault = _profileService.Profiles.Count <= 1 || _profileService.Profiles[0].Id == profile.Id;
                    if (isDefault)
                    {
                        var refreshed = await _cliSyncService.TryRefreshTokenAsync();
                        if (refreshed)
                        {
                            _cliSyncService.SyncToProfile(_profileService, profile.Id);
                            LoggingService.Instance.Log("Auto-refreshed expired OAuth token during usage poll");
                        }
                    }
                }

                var usage = await _apiService.FetchUsageData();
                _profileService.UpdateUsageData(profile.Id, claudeUsage: usage);
                _notificationService.CheckAndNotify(profile, usage);

                // Schedule auto-refresh when session resets (limit lifts)
                ScheduleResetRefresh(usage.SessionResetTime);
            }

            _notificationService.CheckKeyExpiry(profile);

            // Fetch API Console + personal metrics (non-fatal, throttled to every 5 min)
            var lastApiFetch = new DateTime(Interlocked.Read(ref _lastApiFetchTicks));
            var shouldFetchApi = profile.HasAPIConsole
                && (DateTime.UtcNow - lastApiFetch) >= ApiRefreshInterval;
            if (shouldFetchApi)
            {
                try
                {
                    var apiUsage = await _apiService.FetchAPIUsageData(
                        profile.ApiOrganizationId!, profile.ApiSessionKey!);
                    _profileService.UpdateUsageData(profile.Id, apiUsage: apiUsage);
                }
                catch (HttpRequestException ex)
                {
                    LoggingService.Instance.LogWarning($"API Console usage fetch failed (non-fatal): {ex.Message}");
                }

                if (!string.IsNullOrEmpty(profile.ApiUserSearch))
                {
                    try
                    {
                        var today = DateTime.UtcNow.Date;
                        var tomorrow = today.AddDays(1);

                        // Fetch monthly and daily metrics in parallel
                        var monthlyTask = _apiService.FetchClaudeCodeUserMetrics(
                            profile.ApiOrganizationId!, profile.ApiSessionKey!, profile.ApiUserSearch);
                        var dailyTask = _apiService.FetchClaudeCodeUserMetrics(
                            profile.ApiOrganizationId!, profile.ApiSessionKey!, profile.ApiUserSearch,
                            today, tomorrow);
                        await Task.WhenAll(monthlyTask, dailyTask);

                        _profileService.UpdatePersonalMetrics(profile.Id, await monthlyTask);
                        profile.DailyMetrics = await dailyTask;
                    }
                    catch (HttpRequestException ex)
                    {
                        LoggingService.Instance.LogWarning($"Personal metrics fetch failed (non-fatal): {ex.Message}");
                    }
                }

                Interlocked.Exchange(ref _lastApiFetchTicks, DateTime.UtcNow.Ticks);
            }

            // Fetch Claude system status (every 5 minutes)
            if ((DateTime.UtcNow - _lastStatusFetch).TotalMinutes >= Constants.StatusAPI.RefreshIntervalMinutes)
            {
                _cachedStatus = await _statusService.FetchStatusAsync();
                _lastStatusFetch = DateTime.UtcNow;
            }

            _consecutiveRateLimits = 0;
            RefreshCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("Rate limited"))
        {
            // Linear backoff: 5m → 7m → 9m → 11m → ... (capped at 15m)
            _consecutiveRateLimits++;
            var backoffMinutes = Math.Min(5 + (_consecutiveRateLimits - 1) * 2, 15);
            _rateLimitedUntil = DateTime.UtcNow.AddMinutes(backoffMinutes);
            LoggingService.Instance.LogWarning($"Rate limited (attempt {_consecutiveRateLimits}) — backing off {backoffMinutes}m until {_rateLimitedUntil:HH:mm:ss}");
            RefreshFailed?.Invoke(this, $"Rate limited — retrying in {backoffMinutes}m");
            ScheduleRateLimitRefresh();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Usage refresh failed", ex);
            RefreshFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ScheduleRateLimitRefresh()
    {
        _rateLimitTimer?.Stop();
        var delay = _rateLimitedUntil - DateTime.UtcNow;
        if (delay.TotalSeconds <= 0) return;

        _rateLimitTimer = new DispatcherTimer { Interval = delay + TimeSpan.FromSeconds(1) };
        _rateLimitTimer.Tick += (_, _) =>
        {
            _rateLimitTimer?.Stop();
            _rateLimitTimer = null;
            LoggingService.Instance.Log("Rate limit expired — refreshing now");
            RefreshNow();
        };
        _rateLimitTimer.Start();
    }

    private void ScheduleResetRefresh(DateTime resetTime)
    {
        var delay = resetTime - DateTime.UtcNow;
        if (delay.TotalSeconds <= 0 || delay.TotalHours > 6) return; // already passed or too far out

        // Cancel any existing reset timer
        _resetTimer?.Stop();

        _resetTimer = new DispatcherTimer { Interval = delay + TimeSpan.FromSeconds(2) }; // 2s buffer
        _resetTimer.Tick += async (_, _) =>
        {
            _resetTimer?.Stop();
            LoggingService.Instance.Log("Session reset time reached — auto-refreshing");
            await RefreshAsync();
        };
        _resetTimer.Start();
        LoggingService.Instance.Log($"Scheduled auto-refresh at session reset ({resetTime:HH:mm:ss} UTC)");
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
