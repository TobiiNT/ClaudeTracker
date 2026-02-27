using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class AppearanceViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private MenuBarIconStyle _sessionIconStyle;
    [ObservableProperty] private bool _monochromeMode;
    [ObservableProperty] private bool _showIconNames;
    [ObservableProperty] private bool _showRemainingPercentage;
    [ObservableProperty] private bool _useCustomColor;
    [ObservableProperty] private string? _customColorHex;
    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _hasUnsavedChanges;

    // Snapshot of initial values for change detection
    private MenuBarIconStyle _initialIconStyle;
    private bool _initialMonochrome;
    private bool _initialRemaining;
    private bool _initialUseCustomColor;
    private string? _initialCustomColorHex;
    private string _initialTheme;
    private bool _initialized;

    public AppearanceViewModel(IProfileService profileService, ISettingsService settingsService)
    {
        _profileService = profileService;
        _settingsService = settingsService;

        var config = _profileService.ActiveProfile?.IconConfig ?? MenuBarIconConfiguration.Default;
        var sessionConfig = config.GetConfig(MenuBarMetricType.Session);

        SessionIconStyle = sessionConfig?.IconStyle ?? MenuBarIconStyle.Battery;
        MonochromeMode = config.MonochromeMode;
        ShowIconNames = config.ShowIconNames;
        ShowRemainingPercentage = config.ShowRemainingPercentage;
        UseCustomColor = config.UseCustomColor;
        CustomColorHex = config.CustomColorHex;
        Theme = _settingsService.Settings.Theme;

        // Snapshot
        _initialIconStyle = SessionIconStyle;
        _initialMonochrome = MonochromeMode;
        _initialRemaining = ShowRemainingPercentage;
        _initialUseCustomColor = UseCustomColor;
        _initialCustomColorHex = CustomColorHex;
        _initialTheme = Theme;
        _initialized = true;
    }

    partial void OnSessionIconStyleChanged(MenuBarIconStyle value) => DetectChanges();
    partial void OnMonochromeModeChanged(bool value) => DetectChanges();
    partial void OnShowIconNamesChanged(bool value) => DetectChanges();
    partial void OnShowRemainingPercentageChanged(bool value) => DetectChanges();
    partial void OnUseCustomColorChanged(bool value) => DetectChanges();
    partial void OnCustomColorHexChanged(string? value) => DetectChanges();
    partial void OnThemeChanged(string value) => DetectChanges();

    private void DetectChanges()
    {
        if (!_initialized) return;
        HasUnsavedChanges =
            SessionIconStyle != _initialIconStyle ||
            MonochromeMode != _initialMonochrome ||
            ShowRemainingPercentage != _initialRemaining ||
            UseCustomColor != _initialUseCustomColor ||
            CustomColorHex != _initialCustomColorHex ||
            Theme != _initialTheme;
    }

    [RelayCommand]
    private void Save()
    {
        // Save icon config
        var profile = _profileService.ActiveProfile;
        if (profile != null)
        {
            profile.IconConfig.MonochromeMode = MonochromeMode;
            profile.IconConfig.ShowIconNames = ShowIconNames;
            profile.IconConfig.ShowRemainingPercentage = ShowRemainingPercentage;
            profile.IconConfig.UseCustomColor = UseCustomColor;
            profile.IconConfig.CustomColorHex = CustomColorHex;

            var sessionConfig = profile.IconConfig.GetConfig(MenuBarMetricType.Session);
            if (sessionConfig != null)
            {
                sessionConfig.IconStyle = SessionIconStyle;
                profile.IconConfig.UpdateConfig(sessionConfig);
            }

            _profileService.UpdateProfile(profile);
        }

        // Save theme
        _settingsService.Settings.Theme = Theme;
        _settingsService.Save();

        // Apply theme immediately
        App.ApplyTheme(Theme);

        // Update snapshot
        _initialIconStyle = SessionIconStyle;
        _initialMonochrome = MonochromeMode;
        _initialRemaining = ShowRemainingPercentage;
        _initialUseCustomColor = UseCustomColor;
        _initialCustomColorHex = CustomColorHex;
        _initialTheme = Theme;
        HasUnsavedChanges = false;
    }
}
