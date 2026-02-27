using System.Windows.Threading;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class AutoStartSessionService : IDisposable
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly INotificationService _notificationService;
    private DispatcherTimer? _timer;
    private bool _isProcessing;
    private readonly HashSet<Guid> _recentlyAutoStarted = new();

    public AutoStartSessionService(
        IClaudeApiService apiService,
        IProfileService profileService,
        INotificationService notificationService)
    {
        _apiService = apiService;
        _profileService = profileService;
        _notificationService = notificationService;

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start()
    {
        if (_timer != null) return;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Constants.AutoStart.CheckIntervalMinutes)
        };
        _timer.Tick += async (_, _) => await CheckAndAutoStart();
        _timer.Start();

        LoggingService.Instance.Log("AutoStartSessionService started");
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        LoggingService.Instance.Log("AutoStartSessionService stopped");
    }

    private async Task CheckAndAutoStart()
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            var profile = _profileService.ActiveProfile;
            if (profile == null || !profile.AutoStartSessionEnabled) return;
            if (string.IsNullOrEmpty(profile.ClaudeSessionKey)) return;

            var usage = profile.ClaudeUsage;
            if (usage == null || usage.SessionPercentage > 0) return;

            // Check if recently auto-started (prevent duplicates)
            if (_recentlyAutoStarted.Contains(profile.Id)) return;

            LoggingService.Instance.Log($"Auto-starting session for profile '{profile.Name}'");

            await _apiService.SendInitializationMessage();

            _recentlyAutoStarted.Add(profile.Id);
            _notificationService.SendNotification(
                "Session Auto-Started",
                $"A new session has been started for {profile.Name}");

            // Clear the auto-start flag after reset time passes
            _ = Task.Delay(TimeSpan.FromMinutes(10)).ContinueWith(_ =>
                _recentlyAutoStarted.Remove(profile.Id));
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Auto-start failed", ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            LoggingService.Instance.Log("System resumed from sleep - checking auto-start");
            await CheckAndAutoStart();
        }
    }

    public void Dispose()
    {
        Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
