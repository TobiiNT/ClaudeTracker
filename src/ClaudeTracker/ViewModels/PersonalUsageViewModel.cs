using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class PersonalUsageViewModel : ObservableObject
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly ClaudeCodeSyncService _cliSyncService;

    [ObservableProperty] private string _sessionKey = "";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private bool _testSuccess;
    [ObservableProperty] private bool _showOrgPicker;
    [ObservableProperty] private AccountInfo? _selectedOrg;
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private string _connectedLabel = "Connected";
    [ObservableProperty] private string _connectedDetail = "";
    [ObservableProperty] private string _autoDetectStatusText = "";
    [ObservableProperty] private bool _autoDetectSuccess;

    public ObservableCollection<AccountInfo> Organizations { get; } = new();

    /// <summary>True if the active profile is the first (default) profile or the only profile.</summary>
    public bool IsDefaultProfile
    {
        get
        {
            var profiles = _profileService.Profiles;
            if (profiles.Count <= 1) return true;
            var active = _profileService.ActiveProfile;
            return active != null && profiles.Count > 0 && profiles[0].Id == active.Id;
        }
    }

    public PersonalUsageViewModel(
        IClaudeApiService apiService,
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ClaudeCodeSyncService cliSyncService)
    {
        _apiService = apiService;
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;
        _cliSyncService = cliSyncService;

        var profile = _profileService.ActiveProfile;
        IsConfigured = profile?.HasClaudeSessionKey == true || !string.IsNullOrEmpty(profile?.CliCredentialsJSON);
        if (profile?.ClaudeSessionKey != null)
            SessionKey = profile.ClaudeSessionKey;

        UpdateConnectedInfo();
    }

    [RelayCommand]
    private async Task AutoDetect()
    {
        AutoDetectStatusText = "";
        AutoDetectSuccess = false;

        try
        {
            var (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();

            if (string.IsNullOrEmpty(token))
            {
                AutoDetectStatusText = "No Claude OAuth credentials found.\nMake sure Claude Code is installed and logged in.";
                return;
            }

            // If expired, try silent refresh before giving up
            if (isExpired)
            {
                var refreshed = await _cliSyncService.TryRefreshTokenAsync();
                if (refreshed)
                {
                    // Re-read refreshed token info
                    (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();
                }

                if (isExpired)
                {
                    AutoDetectStatusText = "Claude OAuth token is expired and refresh failed.\nRun 'claude auth login' to re-authenticate.";
                    return;
                }
            }

            var profile = _profileService.ActiveProfile;
            if (profile == null)
            {
                AutoDetectStatusText = "No active profile";
                return;
            }

            var success = _cliSyncService.SyncToProfile(_profileService, profile.Id);
            if (!success)
            {
                AutoDetectStatusText = "Failed to sync credentials";
                return;
            }

            try
            {
                await _apiService.FetchUsageData();
            }
            catch
            {
                // Token synced but usage fetch may need org ID setup — that's OK
            }

            AutoDetectSuccess = true;
            var planLabel = !string.IsNullOrEmpty(subType) ? $" ({subType})" : "";
            AutoDetectStatusText = $"Connected{planLabel}";
            IsConfigured = true;
            UpdateConnectedInfo();
        }
        catch (Exception ex)
        {
            AutoDetectStatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(SessionKey)) return;

        IsTesting = true;
        TestStatus = "Testing connection...";
        TestSuccess = false;

        try
        {
            var orgs = await _apiService.TestSessionKey(SessionKey.Trim());

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
                TestStatus = $"Found {orgs.Count} organizations. Please select one.";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Connection failed: {ex.Message}";
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

        profile.ClaudeSessionKey = SessionKey.Trim();
        profile.OrganizationId = SelectedOrg.Uuid;
        _profileService.UpdateProfile(profile);

        TestStatus = $"Connected to {SelectedOrg.Name}";
        TestSuccess = true;
        IsConfigured = true;
        ShowOrgPicker = false;
        UpdateConnectedInfo();

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void Disconnect()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        profile.ClaudeSessionKey = null;
        profile.OrganizationId = null;
        profile.CliCredentialsJSON = null;
        profile.HasClaudeOAuth = false;
        profile.ClaudeOAuthSyncedAt = null;
        profile.ClaudeUsage = null;
        _profileService.UpdateProfile(profile);

        SessionKey = "";
        IsConfigured = false;
        TestStatus = "";
        TestSuccess = false;
        AutoDetectStatusText = "";
        AutoDetectSuccess = false;
        ConnectedLabel = "";
        ConnectedDetail = "";

        _refreshCoordinator.InvalidateApiCache();
        _refreshCoordinator.RefreshNow();
    }

    private void UpdateConnectedInfo()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        if (!string.IsNullOrEmpty(profile.CliCredentialsJSON))
        {
            var parsed = _cliSyncService.ParseCredentials(profile.CliCredentialsJSON);
            var subType = parsed?.ClaudeOAuth?.SubscriptionType;
            ConnectedLabel = "Connected via Claude OAuth";
            ConnectedDetail = !string.IsNullOrEmpty(subType)
                ? $"Plan: {subType}"
                : "Claude OAuth token active";
        }
        else if (profile.HasClaudeSessionKey)
        {
            ConnectedLabel = "Connected via Claude Session Key";
            ConnectedDetail = !string.IsNullOrEmpty(profile.OrganizationId)
                ? $"Org: {profile.OrganizationId[..Math.Min(8, profile.OrganizationId.Length)]}..."
                : "";
        }
    }
}
