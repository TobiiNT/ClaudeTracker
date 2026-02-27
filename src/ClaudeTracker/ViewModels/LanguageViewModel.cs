using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Services;

namespace ClaudeTracker.ViewModels;

public partial class LanguageViewModel : ObservableObject
{
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _selectedLanguage;

    public (string Code, string Name)[] SupportedLanguages => LanguageService.SupportedLanguages;

    public LanguageViewModel(LanguageService languageService)
    {
        _languageService = languageService;
        _selectedLanguage = _languageService.CurrentLanguage;
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        _languageService.SetLanguage(value);
    }
}
