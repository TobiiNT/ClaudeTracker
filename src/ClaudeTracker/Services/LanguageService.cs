using System.Globalization;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

public class LanguageService
{
    private readonly ISettingsService _settingsService;

    public static readonly (string Code, string Name)[] SupportedLanguages =
    [
        ("en", "English"),
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fr", "Français"),
        ("it", "Italiano"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("pt", "Português")
    ];

    public string CurrentLanguage => _settingsService.Settings.AppLanguage;

    public LanguageService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        ApplyLanguage(_settingsService.Settings.AppLanguage);
    }

    public void SetLanguage(string languageCode)
    {
        _settingsService.Settings.AppLanguage = languageCode;
        _settingsService.Save();
        ApplyLanguage(languageCode);
        LoggingService.Instance.Log($"Language changed to: {languageCode}");
    }

    private static void ApplyLanguage(string languageCode)
    {
        try
        {
            var culture = new CultureInfo(languageCode);
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"Failed to apply language '{languageCode}'", ex);
        }
    }
}
