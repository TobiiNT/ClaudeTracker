using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels.Settings;

public partial class PermissionsSettingsViewModel : ObservableObject
{
    private readonly IClaudeCodeSettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<PermissionRuleEntry> _rules = new();

    [ObservableProperty]
    private bool _isEmpty = true;

    public PermissionsSettingsViewModel(IClaudeCodeSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadRules();
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadRules();
    }

    [RelayCommand]
    private void DeleteRule(string key)
    {
        _settingsService.RemovePermission(key);
        LoadRules();
    }

    private void LoadRules()
    {
        var loaded = _settingsService.GetPermissions();
        Rules = new ObservableCollection<PermissionRuleEntry>(loaded);
        IsEmpty = Rules.Count == 0;
    }
}
