using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class ApiBillingViewModel : ObservableObject
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private bool _testSuccess;
    [ObservableProperty] private bool _showOrgPicker;
    [ObservableProperty] private APIOrganization? _selectedOrg;
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private bool _showUserPicker;
    [ObservableProperty] private ClaudeCodeUserMetrics? _selectedUser;

    public ObservableCollection<APIOrganization> Organizations { get; } = new();
    public ObservableCollection<ClaudeCodeUserMetrics> ClaudeCodeUsers { get; } = new();

    public ApiBillingViewModel(IClaudeApiService apiService, IProfileService profileService, ISettingsService settingsService)
    {
        _apiService = apiService;
        _profileService = profileService;
        _settingsService = settingsService;

        var profile = _profileService.ActiveProfile;
        IsConfigured = profile?.HasAPIConsole ?? false;
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return;

        IsTesting = true;
        TestStatus = "Testing connection...";

        try
        {
            var orgs = await _apiService.FetchConsoleOrganizations(ApiKey.Trim());

            Organizations.Clear();
            foreach (var org in orgs)
                Organizations.Add(org);

            if (orgs.Count == 1)
            {
                SelectedOrg = orgs[0];
                await SaveConfiguration();
            }
            else
            {
                ShowOrgPicker = true;
                TestStatus = $"Found {orgs.Count} organizations.";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Failed: {ex.Message}";
            TestSuccess = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SelectOrganization()
    {
        if (SelectedOrg == null) return;
        await SaveConfiguration();
    }

    private async Task SaveConfiguration()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null || SelectedOrg == null) return;

        var credentials = _profileService.LoadCredentials(profile.Id);
        credentials.ApiSessionKey = ApiKey.Trim();
        credentials.ApiOrganizationId = SelectedOrg.Id;
        _profileService.SaveCredentials(profile.Id, credentials);

        TestStatus = $"Connected to {SelectedOrg.DisplayName}";
        TestSuccess = true;
        IsConfigured = true;
        ShowOrgPicker = false;

        // Auto-fetch Claude Code users for identity picker
        try
        {
            var users = await _apiService.FetchClaudeCodeAllUsers(SelectedOrg.Id, ApiKey.Trim());
            ClaudeCodeUsers.Clear();
            foreach (var user in users)
                ClaudeCodeUsers.Add(user);

            if (users.Count > 0)
            {
                ShowUserPicker = true;
                if (!string.IsNullOrEmpty(profile.ApiUserSearch))
                    SelectedUser = users.FirstOrDefault(u => u.DisplayName == profile.ApiUserSearch);
            }
        }
        catch (Exception ex)
        {
            Services.LoggingService.Instance.LogWarning($"Failed to fetch Claude Code users: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveUserSelection()
    {
        if (SelectedUser == null) return;
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        profile.ApiUserSearch = SelectedUser.DisplayName;
        _settingsService.Save();

        ShowUserPicker = false;
        TestStatus = $"Tracking: {SelectedUser.DisplayName}";
        TestSuccess = true;
    }

    [RelayCommand]
    private void Disconnect()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var credentials = _profileService.LoadCredentials(profile.Id);
        credentials.ApiSessionKey = null;
        credentials.ApiOrganizationId = null;
        _profileService.SaveCredentials(profile.Id, credentials);

        ApiKey = "";
        IsConfigured = false;
        TestStatus = "";
    }
}
