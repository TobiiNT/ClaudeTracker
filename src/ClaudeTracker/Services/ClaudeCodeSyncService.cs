using System.Net.Http;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeCodeSyncService
{
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public ClaudeCodeSyncService(ICredentialService credentialService, HttpClient? httpClient = null)
    {
        _credentialService = credentialService;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        try
        {
            var json = ReadSystemCredentials();
            var parsed = ParseCredentials(json);
            if (parsed?.ClaudeAiOauth == null)
            {
                LoggingService.Instance.Log("No credentials found for token refresh");
                return false;
            }

            var refreshToken = parsed.ClaudeAiOauth.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                LoggingService.Instance.Log("No refresh token available");
                return false;
            }

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = Constants.APIEndpoints.OAuthClientId
            });

            var response = await _httpClient.PostAsync(Constants.APIEndpoints.OAuthTokenEndpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.Log($"Token refresh failed with status {(int)response.StatusCode}");
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var accessTokenEl))
            {
                LoggingService.Instance.Log("Token refresh response missing access_token");
                return false;
            }

            var newAccessToken = accessTokenEl.GetString();
            if (string.IsNullOrEmpty(newAccessToken))
            {
                LoggingService.Instance.Log("Token refresh response has empty access_token");
                return false;
            }

            parsed.ClaudeAiOauth.AccessToken = newAccessToken;

            if (root.TryGetProperty("refresh_token", out var newRefreshEl))
            {
                var newRefresh = newRefreshEl.GetString();
                if (!string.IsNullOrEmpty(newRefresh))
                    parsed.ClaudeAiOauth.RefreshToken = newRefresh;
            }

            if (root.TryGetProperty("expires_in", out var expiresInEl) && expiresInEl.TryGetInt64(out var expiresInSeconds))
            {
                parsed.ClaudeAiOauth.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (expiresInSeconds * 1000);
            }

            var updatedJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
            _credentialService.WriteCliCredentials(updatedJson);

            LoggingService.Instance.Log("OAuth token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to refresh OAuth token", ex);
            return false;
        }
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
