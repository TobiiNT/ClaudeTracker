using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;
using ClaudeTracker.TrayIcon;

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

        services.AddTransient<AboutViewModel>();

        // HttpClient
        services.AddHttpClient("Claude", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
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
