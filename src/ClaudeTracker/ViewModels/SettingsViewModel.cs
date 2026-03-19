using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeTracker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedTab = "PersonalUsage";

    [ObservableProperty]
    private object? _currentView;

    public PersonalUsageViewModel PersonalUsage { get; }
    public ClaudeOAuthViewModel ClaudeOAuth { get; }
    public AppearanceViewModel Appearance { get; }
    public GeneralSettingsViewModel GeneralSettings { get; }
    public ProfilesViewModel Profiles { get; }
    public LanguageViewModel Language { get; }
    public AboutViewModel About { get; }

    public SettingsViewModel(
        PersonalUsageViewModel personalUsage,
        ClaudeOAuthViewModel claudeOAuth,
        AppearanceViewModel appearance,
        GeneralSettingsViewModel generalSettings,
        ProfilesViewModel profiles,
        LanguageViewModel language,
        AboutViewModel about)
    {
        PersonalUsage = personalUsage;
        ClaudeOAuth = claudeOAuth;
        Appearance = appearance;
        GeneralSettings = generalSettings;
        Profiles = profiles;
        Language = language;
        About = about;
    }

    partial void OnSelectedTabChanged(string value)
    {
        CurrentView = value switch
        {
            "PersonalUsage" => PersonalUsage,
            "ClaudeOAuth" => ClaudeOAuth,
            "Appearance" => Appearance,
            "General" => GeneralSettings,
            "Profiles" => Profiles,
            "Language" => Language,
            "About" => About,
            _ => PersonalUsage
        };
    }
}
