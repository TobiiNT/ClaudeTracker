using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class CliAccountViewModel : ObservableObject
{
    private readonly ClaudeCodeSyncService _syncService;
    private readonly IProfileService _profileService;

    [ObservableProperty] private bool _hasCli;
    [ObservableProperty] private bool _isTokenValid;
    [ObservableProperty] private string _tokenStatus = "Not synced";
    [ObservableProperty] private string _expiresAtText = "";
    [ObservableProperty] private bool _isSyncing;

    public CliAccountViewModel(ClaudeCodeSyncService syncService, IProfileService profileService)
    {
        _syncService = syncService;
        _profileService = profileService;
        RefreshStatus();
    }

    [RelayCommand]
    private void Sync()
    {
        IsSyncing = true;

        try
        {
            var profile = _profileService.ActiveProfile;
            if (profile == null)
            {
                TokenStatus = "No active profile";
                return;
            }

            var success = _syncService.SyncToProfile(_profileService, profile.Id);
            if (success)
            {
                TokenStatus = "Synced successfully";
                RefreshStatus();
            }
            else
            {
                TokenStatus = "Sync failed - credentials not found or expired";
            }
        }
        catch (Exception ex)
        {
            TokenStatus = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void RefreshStatus()
    {
        var (token, orgUuid, subType, isExpired, expiresAt) = _syncService.GetTokenInfo();

        HasCli = token != null;
        IsTokenValid = token != null && !isExpired;

        if (token == null)
        {
            TokenStatus = "No CLI credentials found at ~/.claude/.credentials.json";
        }
        else if (isExpired)
        {
            TokenStatus = "Token expired - run 'claude' to refresh";
        }
        else
        {
            var planLabel = !string.IsNullOrEmpty(subType) ? $" ({subType})" : "";
            TokenStatus = $"Token valid{planLabel}";
        }

        ExpiresAtText = expiresAt.HasValue
            ? $"Expires: {expiresAt.Value.ToLocalTime():g}"
            : "";
    }
}
