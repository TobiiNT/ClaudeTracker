using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

public class ClaudeCodeSyncService
{
    private readonly ICredentialService _credentialService;

    public ClaudeCodeSyncService(ICredentialService credentialService)
    {
        _credentialService = credentialService;
    }

    public string? ReadSystemCredentials()
    {
        return _credentialService.ReadCliCredentials();
    }

    public CliCredentialsJson? ParseCredentials(string? credentialsJson)
    {
        if (string.IsNullOrEmpty(credentialsJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<CliCredentialsJson>(credentialsJson);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to parse CLI credentials", ex);
            return null;
        }
    }

    public string? ExtractAccessToken(string? credentialsJson)
    {
        return ParseCredentials(credentialsJson)?.ClaudeAiOauth?.AccessToken;
    }

    public string? ExtractOrganizationUuid(string? credentialsJson)
    {
        return ParseCredentials(credentialsJson)?.OrganizationUuid;
    }

    public bool IsTokenExpired(string? credentialsJson)
    {
        if (string.IsNullOrEmpty(credentialsJson)) return true;

        try
        {
            var parsed = ParseCredentials(credentialsJson);
            if (parsed?.ClaudeAiOauth?.ExpiresAt is long expiresAtMs)
            {
                // expiresAt is in milliseconds
                var expiryTime = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs);
                return expiryTime <= DateTimeOffset.UtcNow;
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    public (string? accessToken, string? orgUuid, string? subscriptionType, bool isExpired, DateTime? expiresAt) GetTokenInfo()
    {
        var json = ReadSystemCredentials();
        if (string.IsNullOrEmpty(json))
            return (null, null, null, true, null);

        var parsed = ParseCredentials(json);
        if (parsed?.ClaudeAiOauth == null)
            return (null, null, null, true, null);

        var token = parsed.ClaudeAiOauth.AccessToken;
        var orgUuid = parsed.OrganizationUuid;
        var subType = parsed.ClaudeAiOauth.SubscriptionType;
        var expired = IsTokenExpired(json);

        DateTime? expiresAt = null;
        if (parsed.ClaudeAiOauth.ExpiresAt is long exp)
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(exp).UtcDateTime;

        return (token, orgUuid, subType, expired, expiresAt);
    }

    public bool SyncToProfile(IProfileService profileService, Guid profileId)
    {
        try
        {
            var json = ReadSystemCredentials();
            if (string.IsNullOrEmpty(json))
            {
                LoggingService.Instance.Log("No CLI credentials found at ~/.claude/.credentials.json");
                return false;
            }

            if (IsTokenExpired(json))
            {
                LoggingService.Instance.Log("CLI OAuth token is expired");
                return false;
            }

            var token = ExtractAccessToken(json);
            if (string.IsNullOrEmpty(token))
            {
                LoggingService.Instance.Log("Could not extract access token from CLI credentials");
                return false;
            }

            var orgUuid = ExtractOrganizationUuid(json);

            var profile = profileService.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return false;

            profile.CliCredentialsJSON = json;
            profile.HasCliAccount = true;
            profile.CliAccountSyncedAt = DateTime.UtcNow;

            // Also set the organization ID if found
            if (!string.IsNullOrEmpty(orgUuid) && string.IsNullOrEmpty(profile.OrganizationId))
                profile.OrganizationId = orgUuid;

            profileService.UpdateProfile(profile);

            LoggingService.Instance.Log($"Synced CLI credentials to profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to sync CLI credentials", ex);
            return false;
        }
    }
}
