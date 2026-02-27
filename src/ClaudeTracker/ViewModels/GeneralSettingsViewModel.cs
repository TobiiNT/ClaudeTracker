using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly LaunchAtLoginService _launchAtLogin;

    [ObservableProperty] private double _refreshInterval;
    [ObservableProperty] private bool _autoStartSession;
    [ObservableProperty] private bool _launchAtLoginEnabled;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _threshold75;
    [ObservableProperty] private bool _threshold90;
    [ObservableProperty] private bool _threshold95;
    [ObservableProperty] private bool _checkOverageLimit;
    [ObservableProperty] private bool _hasUnsavedChanges;

    // Snapshot
    private double _initialRefresh;
    private bool _initialAutoStart;
    private bool _initialLaunch;
    private bool _initialNotify;
    private bool _initialT75;
    private bool _initialT90;
    private bool _initialT95;
    private bool _initialOverage;
    private bool _initialized;

    public GeneralSettingsViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        LaunchAtLoginService launchAtLogin)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;
        _launchAtLogin = launchAtLogin;

        var profile = _profileService.ActiveProfile;
        if (profile != null)
        {
            RefreshInterval = profile.RefreshInterval;
            AutoStartSession = profile.AutoStartSessionEnabled;
            NotificationsEnabled = profile.NotificationSettings.Enabled;
            Threshold75 = profile.NotificationSettings.Threshold75Enabled;
            Threshold90 = profile.NotificationSettings.Threshold90Enabled;
            Threshold95 = profile.NotificationSettings.Threshold95Enabled;
            CheckOverageLimit = profile.CheckOverageLimitEnabled;
        }

        LaunchAtLoginEnabled = _launchAtLogin.IsEnabled;

        // Snapshot
        _initialRefresh = RefreshInterval;
        _initialAutoStart = AutoStartSession;
        _initialLaunch = LaunchAtLoginEnabled;
        _initialNotify = NotificationsEnabled;
        _initialT75 = Threshold75;
        _initialT90 = Threshold90;
        _initialT95 = Threshold95;
        _initialOverage = CheckOverageLimit;
        _initialized = true;
    }

    partial void OnRefreshIntervalChanged(double value) => DetectChanges();
    partial void OnAutoStartSessionChanged(bool value) => DetectChanges();
    partial void OnLaunchAtLoginEnabledChanged(bool value) => DetectChanges();
    partial void OnNotificationsEnabledChanged(bool value) => DetectChanges();
    partial void OnThreshold75Changed(bool value) => DetectChanges();
    partial void OnThreshold90Changed(bool value) => DetectChanges();
    partial void OnThreshold95Changed(bool value) => DetectChanges();
    partial void OnCheckOverageLimitChanged(bool value) => DetectChanges();

    private void DetectChanges()
    {
        if (!_initialized) return;
        HasUnsavedChanges =
            RefreshInterval != _initialRefresh ||
            AutoStartSession != _initialAutoStart ||
            LaunchAtLoginEnabled != _initialLaunch ||
            NotificationsEnabled != _initialNotify ||
            Threshold75 != _initialT75 ||
            Threshold90 != _initialT90 ||
            Threshold95 != _initialT95 ||
            CheckOverageLimit != _initialOverage;
    }

    [RelayCommand]
    private void Save()
    {
        var profile = _profileService.ActiveProfile;
        if (profile != null)
        {
            profile.RefreshInterval = RefreshInterval;
            profile.AutoStartSessionEnabled = AutoStartSession;
            profile.NotificationSettings.Enabled = NotificationsEnabled;
            profile.NotificationSettings.Threshold75Enabled = Threshold75;
            profile.NotificationSettings.Threshold90Enabled = Threshold90;
            profile.NotificationSettings.Threshold95Enabled = Threshold95;
            profile.CheckOverageLimitEnabled = CheckOverageLimit;
            _profileService.UpdateProfile(profile);

            _refreshCoordinator.UpdateInterval(RefreshInterval);
        }

        _launchAtLogin.IsEnabled = LaunchAtLoginEnabled;

        // Update snapshot
        _initialRefresh = RefreshInterval;
        _initialAutoStart = AutoStartSession;
        _initialLaunch = LaunchAtLoginEnabled;
        _initialNotify = NotificationsEnabled;
        _initialT75 = Threshold75;
        _initialT90 = Threshold90;
        _initialT95 = Threshold95;
        _initialOverage = CheckOverageLimit;
        HasUnsavedChanges = false;
    }
}
