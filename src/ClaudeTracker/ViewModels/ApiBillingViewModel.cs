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
    private readonly IUsageRefreshCoordinator _refreshCoordinator;

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private bool _testSuccess;
    [ObservableProperty] private bool _showOrgPicker;
    [ObservableProperty] private APIOrganization? _selectedOrg;
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private bool _showUserPicker;
    [ObservableProperty] private bool _isLoadingUsers;
    [ObservableProperty] private ClaudeCodeUserMetrics? _selectedUser;
    [ObservableProperty] private string _trackedUserLabel = "";

    public ObservableCollection<APIOrganization> Organizations { get; } = new();
    public ObservableCollection<ClaudeCodeUserMetrics> ClaudeCodeUsers { get; } = new();

    public ApiBillingViewModel(IClaudeApiService apiService, IProfileService profileService,
        ISettingsService settingsService, IUsageRefreshCoordinator refreshCoordinator)
    {
        _apiService = apiService;
        _profileService = profileService;
        _settingsService = settingsService;
        _refreshCoordinator = refreshCoordinator;

        var profile = _profileService.ActiveProfile;
        IsConfigured = profile?.HasAPIConsole ?? false;
        TrackedUserLabel = profile?.ApiUserSearch ?? "";
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return;

        IsTesting = true;
        TestStatus = "Testing connection...";
        ShowOrgPicker = false;
        ShowUserPicker = false;

        try
        {
            var orgs = await _apiService.FetchConsoleOrganizations(ApiKey.Trim());

            Organizations.Clear();
            foreach (var org in orgs)
                Organizations.Add(org);

            if (orgs.Count == 1)
            {
                SelectedOrg = orgs[0];
                TestStatus = $"Connected to {orgs[0].DisplayName}";
                TestSuccess = true;
                await FetchUsersForOrg(orgs[0]);
            }
            else if (orgs.Count > 1)
            {
                ShowOrgPicker = true;
                TestStatus = "Select your organization:";
                TestSuccess = true;
            }
            else
            {
                TestStatus = "No organizations found.";
                TestSuccess = false;
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

    /// <summary>
    /// Called when org is selected from the dropdown — immediately fetches users.
    /// </summary>
    [RelayCommand]
    private async Task SelectOrganization()
    {
        if (SelectedOrg == null) return;
        TestStatus = $"Connected to {SelectedOrg.DisplayName}";
        TestSuccess = true;
        await FetchUsersForOrg(SelectedOrg);
    }

    private async Task FetchUsersForOrg(APIOrganization org)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        IsLoadingUsers = true;
        ClaudeCodeUsers.Clear();
        ShowUserPicker = false;

        try
        {
            var users = await _apiService.FetchClaudeCodeAllUsers(org.Id, ApiKey.Trim());
            foreach (var user in users)
                ClaudeCodeUsers.Add(user);

            if (users.Count > 0)
            {
                ShowUserPicker = true;
                if (!string.IsNullOrEmpty(profile.ApiUserSearch))
                    SelectedUser = users.FirstOrDefault(u => u.DisplayName == profile.ApiUserSearch);
            }
            else
            {
                TestStatus = $"Connected to {org.DisplayName}, but no users found.";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Connected, but failed to load users: {ex.Message}";
            TestSuccess = false;
            Services.LoggingService.Instance.LogWarning($"Failed to fetch Claude Code users: {ex.Message}");
        }
        finally
        {
            IsLoadingUsers = false;
        }
    }

    /// <summary>
    /// Final confirm — saves org + user selection together.
    /// </summary>
    [RelayCommand]
    private void SaveUserSelection()
    {
        if (SelectedUser == null || SelectedOrg == null) return;
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        // Save credentials
        var credentials = _profileService.LoadCredentials(profile.Id);
        credentials.ApiSessionKey = ApiKey.Trim();
        credentials.ApiOrganizationId = SelectedOrg.Id;
        _profileService.SaveCredentials(profile.Id, credentials);

        // Save user selection
        profile.ApiUserSearch = SelectedUser.DisplayName;
        _settingsService.Save();

        ShowUserPicker = false;
        ShowOrgPicker = false;
        IsConfigured = true;
        TrackedUserLabel = SelectedUser.DisplayName;
        TestStatus = $"Tracking: {SelectedUser.DisplayName}";
        TestSuccess = true;

        _refreshCoordinator.InvalidateApiCache();
        _refreshCoordinator.RefreshNow();
    }

    [RelayCommand]
    private async Task ChangeUser()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var credentials = _profileService.LoadCredentials(profile.Id);
        if (string.IsNullOrEmpty(credentials.ApiSessionKey) || string.IsNullOrEmpty(credentials.ApiOrganizationId))
        {
            TestStatus = "No API credentials configured.";
            TestSuccess = false;
            return;
        }

        ApiKey = credentials.ApiSessionKey;
        IsLoadingUsers = true;

        try
        {
            var users = await _apiService.FetchClaudeCodeAllUsers(credentials.ApiOrganizationId, credentials.ApiSessionKey);
            ClaudeCodeUsers.Clear();
            foreach (var user in users)
                ClaudeCodeUsers.Add(user);

            if (users.Count > 0)
            {
                ShowUserPicker = true;
                if (!string.IsNullOrEmpty(profile.ApiUserSearch))
                    SelectedUser = users.FirstOrDefault(u => u.DisplayName == profile.ApiUserSearch);
            }
            else
            {
                TestStatus = "No users found in this organization.";
                TestSuccess = false;
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Failed to load users: {ex.Message}";
            TestSuccess = false;
            Services.LoggingService.Instance.LogWarning($"Failed to fetch Claude Code users: {ex.Message}");
        }
        finally
        {
            IsLoadingUsers = false;
        }
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

        profile.ApiUserSearch = null;
        profile.ApiUsage = null;
        profile.PersonalMetrics = null;
        profile.DailyMetrics = null;
        _settingsService.Save();

        ApiKey = "";
        IsConfigured = false;
        ShowUserPicker = false;
        ShowOrgPicker = false;
        TrackedUserLabel = "";
        TestStatus = "";

        _refreshCoordinator.InvalidateApiCache();
        _refreshCoordinator.RefreshNow();
    }
}
