using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeTracker.ViewModels;

public partial class HooksSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHookIpcService _hookIpcService;

    [ObservableProperty] private bool _hooksEnabled;
    [ObservableProperty] private bool _permissionPopupsEnabled;
    [ObservableProperty] private bool _elicitationPopupsEnabled;
    [ObservableProperty] private bool _activityFeedEnabled;
    [ObservableProperty] private int _maxFeedEntries;
    [ObservableProperty] private bool _notifyStop;
    [ObservableProperty] private bool _notifyToolError;
    [ObservableProperty] private bool _notifyPermission;
    [ObservableProperty] private bool _notifyIdle;
    [ObservableProperty] private bool _notifyConfigChange;
    [ObservableProperty] private bool _notifySessionLifecycle;
    [ObservableProperty] private bool _notifySubagent;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private bool _isHooksInstalled;
    [ObservableProperty] private string _installStatusText = "";

    public bool IsIpcRunning => _hookIpcService.IsRunning;

    // Snapshot
    private bool _initialHooksEnabled;
    private bool _initialPermissionPopups;
    private bool _initialElicitationPopups;
    private bool _initialActivityFeed;
    private int _initialMaxFeedEntries;
    private bool _initialNotifyStop;
    private bool _initialNotifyToolError;
    private bool _initialNotifyPermission;
    private bool _initialNotifyIdle;
    private bool _initialNotifyConfigChange;
    private bool _initialNotifySessionLifecycle;
    private bool _initialNotifySubagent;
    private bool _initialized;

    public HooksSettingsViewModel(
        ISettingsService settingsService,
        IHookIpcService hookIpcService)
    {
        _settingsService = settingsService;
        _hookIpcService = hookIpcService;

        var settings = _settingsService.Settings;

        HooksEnabled = settings.HooksEnabled;
        PermissionPopupsEnabled = settings.HookPermissionPopupsEnabled;
        ElicitationPopupsEnabled = settings.HookElicitationPopupsEnabled;
        ActivityFeedEnabled = settings.HookActivityFeedEnabled;
        MaxFeedEntries = settings.HookMaxFeedEntries;

        var prefs = settings.HookNotificationPreferences;
        NotifyStop = prefs.GetValueOrDefault("stop", true);
        NotifyToolError = prefs.GetValueOrDefault("toolError", true);
        NotifyPermission = prefs.GetValueOrDefault("permission", true);
        NotifyIdle = prefs.GetValueOrDefault("idle", true);
        NotifyConfigChange = prefs.GetValueOrDefault("configChange", false);
        NotifySessionLifecycle = prefs.GetValueOrDefault("sessionLifecycle", false);
        NotifySubagent = prefs.GetValueOrDefault("subagent", false);

        // Snapshot
        _initialHooksEnabled = HooksEnabled;
        _initialPermissionPopups = PermissionPopupsEnabled;
        _initialElicitationPopups = ElicitationPopupsEnabled;
        _initialActivityFeed = ActivityFeedEnabled;
        _initialMaxFeedEntries = MaxFeedEntries;
        _initialNotifyStop = NotifyStop;
        _initialNotifyToolError = NotifyToolError;
        _initialNotifyPermission = NotifyPermission;
        _initialNotifyIdle = NotifyIdle;
        _initialNotifyConfigChange = NotifyConfigChange;
        _initialNotifySessionLifecycle = NotifySessionLifecycle;
        _initialNotifySubagent = NotifySubagent;
        _initialized = true;
    }

    public void CheckInstallStatus()
    {
        try
        {
            var settingsPath = Utilities.Constants.Hooks.ClaudeSettingsPath;
            if (System.IO.File.Exists(settingsPath))
            {
                var content = System.IO.File.ReadAllText(settingsPath);
                IsHooksInstalled = content.Contains("ClaudeTracker.HookBridge");
            }
            else
            {
                IsHooksInstalled = false;
            }

            InstallStatusText = IsHooksInstalled
                ? $"Hooks installed. IPC server: {(IsIpcRunning ? "running" : "stopped")}"
                : "Hooks not installed. Click Install to register with Claude Code.";
        }
        catch
        {
            InstallStatusText = "Unable to check installation status.";
        }
    }

    partial void OnHooksEnabledChanged(bool value) => DetectChanges();
    partial void OnPermissionPopupsEnabledChanged(bool value) => DetectChanges();
    partial void OnElicitationPopupsEnabledChanged(bool value) => DetectChanges();
    partial void OnActivityFeedEnabledChanged(bool value) => DetectChanges();
    partial void OnMaxFeedEntriesChanged(int value) => DetectChanges();
    partial void OnNotifyStopChanged(bool value) => DetectChanges();
    partial void OnNotifyToolErrorChanged(bool value) => DetectChanges();
    partial void OnNotifyPermissionChanged(bool value) => DetectChanges();
    partial void OnNotifyIdleChanged(bool value) => DetectChanges();
    partial void OnNotifyConfigChangeChanged(bool value) => DetectChanges();
    partial void OnNotifySessionLifecycleChanged(bool value) => DetectChanges();
    partial void OnNotifySubagentChanged(bool value) => DetectChanges();

    private void DetectChanges()
    {
        if (!_initialized) return;
        HasUnsavedChanges =
            HooksEnabled != _initialHooksEnabled ||
            PermissionPopupsEnabled != _initialPermissionPopups ||
            ElicitationPopupsEnabled != _initialElicitationPopups ||
            ActivityFeedEnabled != _initialActivityFeed ||
            MaxFeedEntries != _initialMaxFeedEntries ||
            NotifyStop != _initialNotifyStop ||
            NotifyToolError != _initialNotifyToolError ||
            NotifyPermission != _initialNotifyPermission ||
            NotifyIdle != _initialNotifyIdle ||
            NotifyConfigChange != _initialNotifyConfigChange ||
            NotifySessionLifecycle != _initialNotifySessionLifecycle ||
            NotifySubagent != _initialNotifySubagent;
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Settings;
        var hooksEnabledChanged = settings.HooksEnabled != HooksEnabled;

        settings.HooksEnabled = HooksEnabled;
        settings.HookPermissionPopupsEnabled = PermissionPopupsEnabled;
        settings.HookElicitationPopupsEnabled = ElicitationPopupsEnabled;
        settings.HookActivityFeedEnabled = ActivityFeedEnabled;
        settings.HookMaxFeedEntries = MaxFeedEntries;

        settings.HookNotificationPreferences["stop"] = NotifyStop;
        settings.HookNotificationPreferences["toolError"] = NotifyToolError;
        settings.HookNotificationPreferences["permission"] = NotifyPermission;
        settings.HookNotificationPreferences["idle"] = NotifyIdle;
        settings.HookNotificationPreferences["configChange"] = NotifyConfigChange;
        settings.HookNotificationPreferences["sessionLifecycle"] = NotifySessionLifecycle;
        settings.HookNotificationPreferences["subagent"] = NotifySubagent;

        _settingsService.Save();

        // Start or stop IPC service if HooksEnabled changed
        if (hooksEnabledChanged)
        {
            if (HooksEnabled)
            {
                // Initialize dispatcher if not yet done, then start IPC
                var dispatcher = App.Services.GetRequiredService<IHookEventDispatcher>();
                dispatcher.Initialize();
                _hookIpcService.Start();
            }
            else
            {
                _hookIpcService.Stop();
            }
        }

        // Update snapshot
        _initialHooksEnabled = HooksEnabled;
        _initialPermissionPopups = PermissionPopupsEnabled;
        _initialElicitationPopups = ElicitationPopupsEnabled;
        _initialActivityFeed = ActivityFeedEnabled;
        _initialMaxFeedEntries = MaxFeedEntries;
        _initialNotifyStop = NotifyStop;
        _initialNotifyToolError = NotifyToolError;
        _initialNotifyPermission = NotifyPermission;
        _initialNotifyIdle = NotifyIdle;
        _initialNotifyConfigChange = NotifyConfigChange;
        _initialNotifySessionLifecycle = NotifySessionLifecycle;
        _initialNotifySubagent = NotifySubagent;
        HasUnsavedChanges = false;
    }
}
