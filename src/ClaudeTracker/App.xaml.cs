using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;
using ClaudeTracker.TrayIcon;
using ClaudeTracker.Services.Handlers;
using ClaudeTracker.Services.Observers;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker;

public partial class App : Application
{
    private Mutex? _mutex;
    private IServiceProvider _services = null!;
    private TrayIconManager? _trayIconManager;
    private GlobalHotkeyService? _globalHotkeyService;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global exception handlers — log crashes before the process dies
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Single-instance check
        _mutex = new Mutex(true, Utilities.Constants.MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Claude Tracker is already running.", "Claude Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Configure DI
        var useMock = Environment.GetCommandLineArgs().Contains("--mock", StringComparer.OrdinalIgnoreCase);
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, useMock);
        _services = serviceCollection.BuildServiceProvider();
        Services = _services;

        // Show setup wizard on first launch
        var settingsService = _services.GetRequiredService<ISettingsService>();
        if (!settingsService.Settings.HasCompletedSetup)
        {
            var wizard = new Views.SetupWizardWindow();
            wizard.ShowDialog();
        }

        // Initialize theme
        InitializeTheme();

        // Initialize tray icon
        _trayIconManager = _services.GetRequiredService<TrayIconManager>();
        _trayIconManager.Initialize();

        // Start usage refresh
        var refreshCoordinator = _services.GetRequiredService<IUsageRefreshCoordinator>();
        refreshCoordinator.Start();

        // Start background update check
        var updateService = _services.GetRequiredService<IUpdateService>();
        _ = updateService.StartAsync();

        // Register global hotkey (Ctrl+Shift+C)
        _globalHotkeyService = new GlobalHotkeyService();
        _globalHotkeyService.HotkeyPressed += (_, _) => _trayIconManager?.TogglePopover(fromHotkey: true);
        _globalHotkeyService.Register();

        // Start network monitor
        var networkMonitor = _services.GetRequiredService<INetworkMonitorService>();
        networkMonitor.NetworkRestored += (_, _) => refreshCoordinator.RefreshNow();
        networkMonitor.Start();

        // Start hooks integration
        if (settingsService.Settings.HooksEnabled)
        {
            var hookDispatcher = _services.GetRequiredService<IHookEventDispatcher>();
            hookDispatcher.Initialize();

            var hookIpcService = _services.GetRequiredService<IHookIpcService>();
            hookIpcService.Start();

            // Wire PermissionRequestHandler to show popup UI
            // Track pending popup TCS keyed by request ID so we can resolve on disconnect
            var pendingPopups = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<Models.HookResponse>>();
            var popupOpenedAt = DateTime.MinValue;

            var permHandler = _services.GetServices<IHookEventHandler>()
                .OfType<PermissionRequestHandler>().FirstOrDefault();
            if (permHandler != null)
            {
                permHandler.PermissionRequested += (_, args) =>
                {
                    // Track this TCS for disconnect auto-close
                    pendingPopups[args.Info.SessionId] = args.ResponseSource;
                    popupOpenedAt = DateTime.UtcNow;
                    args.ResponseSource.Task.ContinueWith(_ =>
                    {
                        pendingPopups.TryRemove(args.Info.SessionId, out var _ignored);
                    });

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var popup = new Views.PermissionRequestPopup(args.Info, args.ResponseSource);
                            popup.Show();
                        }
                        catch (Exception ex)
                        {
                            LoggingService.Instance.LogError("Failed to show permission popup", ex);
                            args.ResponseSource.TrySetResult(new Models.HookResponse
                            {
                                RequestId = "",
                                Success = true
                            });
                        }
                    });
                };
            }

            // When pipe disconnects (user answered in terminal), close any pending popup
            hookIpcService.PipeDisconnected += (_, requestId) =>
            {
                LoggingService.Instance.Log($"[Hooks] PipeDisconnected: closing {pendingPopups.Count} pending popup(s)");
                foreach (var kvp in pendingPopups)
                {
                    kvp.Value.TrySetResult(new Models.HookResponse
                    {
                        RequestId = requestId,
                        Success = true,
                        JsonOutput = null
                    });
                }
                pendingPopups.Clear();
            };

            // When a post-execution event arrives while a popup is pending, the user
            // already answered in terminal and Claude Code moved on.
            // Only close popups from the SAME session (other sessions shouldn't interfere).
            hookIpcService.EventArrived += (_, evt) =>
            {
                if (pendingPopups.Count == 0) return;

                // PostToolUse = tool executed (allowed), Stop = session ended,
                // UserPromptSubmit = user sent next prompt (denied/completed)
                if (evt.EventName is not (Events.PostToolUse or Events.PostToolUseFailure
                    or Events.Stop or Events.UserPromptSubmit))
                    return;

                // Extract session_id from the event payload to match against pending popups
                var evtSessionId = "";
                try
                {
                    var payloadNode = System.Text.Json.Nodes.JsonNode.Parse(evt.Payload);
                    evtSessionId = payloadNode?[Fields.SessionId]?.GetValue<string>() ?? "";
                }
                catch { }

                // Only close popups from the same session
                if (!string.IsNullOrEmpty(evtSessionId) && pendingPopups.TryRemove(evtSessionId, out var tcs))
                {
                    LoggingService.Instance.Log($"[Hooks] Event '{evt.EventName}' from session '{evtSessionId}' — auto-closing popup");
                    tcs.TrySetResult(new Models.HookResponse
                    {
                        RequestId = evt.RequestId,
                        Success = true,
                        JsonOutput = null
                    });
                }
            };

            // Start stale session pruning timer
            var sessionTracking = _services.GetRequiredService<ISessionTrackingService>();
            var pruneTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            pruneTimer.Tick += (_, _) => sessionTracking.PruneStale();
            pruneTimer.Start();

            // Wire configurable notifications from hook events
            var activityServiceForNotifications = _services.GetRequiredService<IActivityService>();
            var notificationServiceForHooks = _services.GetRequiredService<INotificationService>();

            activityServiceForNotifications.RecentFeed.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (Models.ActivityEntry entry in e.NewItems)
                {
                    // Suppress notifications while a permission popup is active
                    if (pendingPopups.Count > 0)
                        continue;

                    var prefs = settingsService.Settings.HookNotificationPreferences;
                    var shouldNotify = entry.EventName switch
                    {
                        Events.PostToolUseFailure => prefs.GetValueOrDefault("toolError", true),
                        Events.Notification when entry.RawPayload.Contains("permission_prompt") =>
                            !settingsService.Settings.HookPermissionPopupsEnabled
                            && prefs.GetValueOrDefault("permission", true),
                        Events.Notification when entry.RawPayload.Contains("idle_prompt") =>
                            prefs.GetValueOrDefault("idle", true),
                        Events.ConfigChange => prefs.GetValueOrDefault("configChange", false),
                        Events.SessionStart or Events.SessionEnd => prefs.GetValueOrDefault("sessionLifecycle", false),
                        Events.SubagentStart or Events.SubagentStop => prefs.GetValueOrDefault("subagent", false),
                        _ => false
                    };

                    if (shouldNotify)
                    {
                        var level = entry.EventName == Events.PostToolUseFailure
                            ? Views.NotificationPopup.NotificationLevel.Warning
                            : Views.NotificationPopup.NotificationLevel.Info;

                        ((NotificationService)notificationServiceForHooks).SendNotification(
                            entry.EventName, entry.Summary, level);
                    }
                }
            };

            LoggingService.Instance.Log("Hooks integration initialized");
        }

        // Engagement prompts (delayed to not block startup)
        _ = Task.Delay(5000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                var appSettings = settingsService.Settings;
                if (Views.GitHubStarPromptWindow.ShouldShow(appSettings))
                {
                    new Views.GitHubStarPromptWindow().Show();
                }
                else if (Views.FeedbackPromptWindow.ShouldShow(appSettings))
                {
                    new Views.FeedbackPromptWindow().Show();
                }
            });
        });

        // Listen for system theme changes
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        LoggingService.Instance.Log($"ClaudeTracker started (v{Utilities.Constants.AppVersion})");
    }

    /// <summary>Release the single-instance mutex before Velopack restarts the app.</summary>
    public void ReleaseSingleInstanceMutex()
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _globalHotkeyService?.Dispose();
        _trayIconManager?.Dispose();

        if (_services is IDisposable disposable)
            disposable.Dispose();

        ReleaseSingleInstanceMutex();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LoggingService.Instance.LogFatal("Unhandled UI thread exception", e.Exception);
        LoggingService.Instance.Flush();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LoggingService.Instance.LogFatal("Unhandled AppDomain exception (terminating: {IsTerminating})"
                .Replace("{IsTerminating}", e.IsTerminating.ToString()), ex);
        }
        else
        {
            LoggingService.Instance.LogError($"Unhandled non-CLR exception: {e.ExceptionObject}");
        }
        LoggingService.Instance.Flush();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LoggingService.Instance.LogFatal("Unobserved task exception", e.Exception);
        LoggingService.Instance.Flush();
        e.SetObserved(); // Prevent process termination from unobserved tasks
    }

    private static void ConfigureServices(IServiceCollection services, bool useMock = false)
    {
        // Core services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IProfileService, ProfileService>();
        if (useMock)
            services.AddSingleton<IClaudeApiService, MockClaudeApiService>();
        else
            services.AddSingleton<IClaudeApiService, ClaudeApiService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IUsageRefreshCoordinator, UsageRefreshCoordinator>();
        services.AddSingleton<ClaudeCodeSyncService>();
        services.AddSingleton<AutoStartSessionService>();
        services.AddSingleton<LaunchAtLoginService>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IClaudeStatusService, ClaudeStatusService>();
        services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();

        // Tray
        services.AddSingleton<TrayIconManager>();
        services.AddSingleton<TrayIconRenderer>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PopoverViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PersonalUsageViewModel>();
        services.AddTransient<ApiBillingViewModel>();
        services.AddTransient<CliAccountViewModel>();
        services.AddTransient<AppearanceViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();

        services.AddTransient<HooksSettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        // HttpClient
        services.AddHttpClient("Claude", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // ── Hooks Integration ──
        services.AddSingleton<IHookIpcService, HookIpcService>();
        services.AddSingleton<IHookEventDispatcher, HookEventDispatcher>();

        // Interactive handlers
        services.AddSingleton<IHookEventHandler, PermissionRequestHandler>();
        services.AddSingleton<IHookEventHandler, PreToolUseHandler>();
        services.AddSingleton<IHookEventHandler, ElicitationHandler>();
        services.AddSingleton<IHookEventHandler, UserPromptHandler>();
        services.AddSingleton<IHookEventHandler, StopHandler>();
        services.AddSingleton<IHookEventHandler, SubagentStopHandler>();
        services.AddSingleton<IHookEventHandler, ConfigChangeHandler>();

        // Observers
        services.AddSingleton<IHookEventObserver, ActivityRecorder>();
        services.AddSingleton<IHookEventObserver, SessionTracker>();

        // Services
        services.AddSingleton<IActivityService, ActivityService>();
        services.AddSingleton<ISessionTrackingService, SessionTrackingService>();
    }

    private void InitializeTheme()
    {
        var settingsService = _services.GetRequiredService<ISettingsService>();
        ApplyTheme(settingsService.Settings.Theme);
    }

    public static void ApplyTheme(string theme)
    {
        bool isDark = theme == "dark" || (theme == "auto" && IsSystemDarkMode());

        var paletteHelper = new PaletteHelper();
        var mdTheme = paletteHelper.GetTheme();
        mdTheme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(mdTheme);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Dispatcher.Invoke(() =>
            {
                var settingsService = _services.GetRequiredService<ISettingsService>();
                if (settingsService.Settings.Theme == "auto")
                    InitializeTheme();
            });
        }
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }
}
