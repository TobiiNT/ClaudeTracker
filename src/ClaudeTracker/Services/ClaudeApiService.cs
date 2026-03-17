using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeApiService : IClaudeApiService
{
    private enum AuthenticationType
    {
        ClaudeAISession,
        CliOAuth,
        ConsoleAPISession
    }

    private record AuthInfo(AuthenticationType Type, string Token);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProfileService _profileService;
    private readonly ClaudeCodeSyncService _cliSyncService;
    private readonly SessionKeyValidator _validator = new();

    public ClaudeApiService(
        IHttpClientFactory httpClientFactory,
        IProfileService profileService,
        ClaudeCodeSyncService cliSyncService)
    {
        _httpClientFactory = httpClientFactory;
        _profileService = profileService;
        _cliSyncService = cliSyncService;
    }

    private AuthInfo GetAuthentication()
    {
        var profile = _profileService.ActiveProfile
            ?? throw new InvalidOperationException("No active profile");

        // 1. Try claude.ai session key
        if (!string.IsNullOrEmpty(profile.ClaudeSessionKey))
        {
            var result = _validator.Validate(profile.ClaudeSessionKey);
            if (result.IsValid)
            {
                LoggingService.Instance.Log("Using claude.ai session key");
                return new AuthInfo(AuthenticationType.ClaudeAISession, result.SanitizedKey!);
            }
        }

        // 2. Try saved CLI OAuth
        if (!string.IsNullOrEmpty(profile.CliCredentialsJSON))
        {
            if (!_cliSyncService.IsTokenExpired(profile.CliCredentialsJSON))
            {
                var token = _cliSyncService.ExtractAccessToken(profile.CliCredentialsJSON);
                if (!string.IsNullOrEmpty(token))
                {
                    LoggingService.Instance.Log("Using saved CLI OAuth token");
                    return new AuthInfo(AuthenticationType.CliOAuth, token);
                }
            }
        }

        // 3. Try CLI credentials file (~/.claude/.credentials.json)
        var systemCreds = _cliSyncService.ReadSystemCredentials();
        if (!string.IsNullOrEmpty(systemCreds) && !_cliSyncService.IsTokenExpired(systemCreds))
        {
            var token = _cliSyncService.ExtractAccessToken(systemCreds);
            if (!string.IsNullOrEmpty(token))
            {
                LoggingService.Instance.Log("Using CLI credentials from ~/.claude/.credentials.json");
                return new AuthInfo(AuthenticationType.CliOAuth, token);
            }
        }

        throw new InvalidOperationException("No valid credentials available");
    }

    private HttpRequestMessage BuildAuthenticatedRequest(HttpMethod method, Uri url, AuthInfo auth)
    {
        var request = new HttpRequestMessage(method, url);

        switch (auth.Type)
        {
            case AuthenticationType.ClaudeAISession:
                request.Headers.Add("Cookie", $"sessionKey={auth.Token}");
                request.Headers.Add("Accept", "application/json");
                break;

            case AuthenticationType.CliOAuth:
                request.Headers.Add("Authorization", $"Bearer {auth.Token}");
                request.Headers.Add("User-Agent", "claude-code/2.1.5");
                request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
                break;

            case AuthenticationType.ConsoleAPISession:
                request.Headers.Add("Cookie", $"sessionKey={auth.Token}");
                request.Headers.Add("Accept", "application/json");
                break;
        }

        return request;
    }

    public async Task<List<AccountInfo>> FetchAllOrganizations(string? sessionKey = null)
    {
        var key = sessionKey ?? _profileService.ActiveProfile?.ClaudeSessionKey
            ?? throw new InvalidOperationException("No session key available");

        var url = new UrlBuilder(Constants.APIEndpoints.ClaudeBase)
            .AppendingPath("organizations")
            .Build();

        var client = _httpClientFactory.CreateClient("Claude");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={key}");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request);
        await EnsureSuccessResponse(response, "organizations");

        var json = await response.Content.ReadAsStringAsync();
        var orgs = JsonSerializer.Deserialize<List<AccountInfo>>(json) ?? [];

        if (orgs.Count == 0)
            throw new InvalidOperationException("No organizations found");

        LoggingService.Instance.LogInfo($"Found {orgs.Count} organization(s)");
        return orgs;
    }

    public async Task<List<AccountInfo>> TestSessionKey(string key)
    {
        var result = _validator.Validate(key);
        if (!result.IsValid)
            throw new InvalidOperationException(result.ErrorMessage);

        return await FetchAllOrganizations(result.SanitizedKey);
    }

    public async Task<string> FetchOrganizationId(string? sessionKey = null)
    {
        var profile = _profileService.ActiveProfile;
        if (!string.IsNullOrEmpty(profile?.OrganizationId))
        {
            LoggingService.Instance.LogInfo($"Using stored organization ID: {profile.OrganizationId}");
            return profile.OrganizationId;
        }

        // Try to get org UUID from CLI credentials file
        var cliOrgUuid = _cliSyncService.ExtractOrganizationUuid(_cliSyncService.ReadSystemCredentials());
        if (!string.IsNullOrEmpty(cliOrgUuid))
        {
            LoggingService.Instance.LogInfo($"Using organization ID from CLI credentials: {cliOrgUuid}");
            if (profile != null)
                _profileService.UpdateOrganizationId(cliOrgUuid, profile.Id);
            return cliOrgUuid;
        }

        var orgs = await FetchAllOrganizations(sessionKey);
        var selectedOrg = orgs[0];

        if (profile != null)
            _profileService.UpdateOrganizationId(selectedOrg.Uuid, profile.Id);

        return selectedOrg.Uuid;
    }

    public async Task<ClaudeUsage> FetchUsageData(string sessionKey, string organizationId)
    {
        var data = await PerformClaudeRequest($"/organizations/{organizationId}/usage", sessionKey);
        return ParseUsageResponse(data);
    }

    public async Task<ClaudeUsage> FetchUsageData()
    {
        var auth = GetAuthentication();

        switch (auth.Type)
        {
            case AuthenticationType.ClaudeAISession:
            {
                var orgId = await FetchOrganizationId(auth.Token);
                var usageData = await PerformClaudeRequest($"/organizations/{orgId}/usage", auth.Token);
                var claudeUsage = ParseUsageResponse(usageData);

                var profile = _profileService.ActiveProfile;
                if (profile?.CheckOverageLimitEnabled == true)
                {
                    try
                    {
                        var overageData = await PerformClaudeRequest($"/organizations/{orgId}/overage_spend_limit", auth.Token);
                        var overage = JsonSerializer.Deserialize<OverageSpendLimitResponse>(overageData);
                        if (overage?.IsEnabled == true)
                        {
                            claudeUsage.CostUsed = overage.UsedCredits;
                            claudeUsage.CostLimit = overage.MonthlyCreditLimit;
                            claudeUsage.CostCurrency = overage.Currency;
                        }
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("Unauthorized"))
                    {
                        claudeUsage.CostFetchError = "No permission to access cost data";
                        LoggingService.Instance.LogWarning("Cost data fetch: no permission (organization restriction)");
                    }
                    catch (Exception ex)
                    {
                        claudeUsage.CostFetchError = ex.Message;
                        LoggingService.Instance.LogWarning($"Cost data fetch failed: {ex.Message}");
                    }
                }

                return claudeUsage;
            }

            case AuthenticationType.CliOAuth:
            {
                var url = new Uri(Constants.APIEndpoints.OAuthUsage);
                var client = _httpClientFactory.CreateClient("Claude");
                var request = BuildAuthenticatedRequest(HttpMethod.Get, url, auth);
                var response = await client.SendAsync(request);
                await EnsureSuccessResponse(response, "oauth/usage");
                var data = await response.Content.ReadAsStringAsync();
                return ParseUsageResponse(data);
            }

            default:
                throw new InvalidOperationException("No valid credentials for usage data");
        }
    }

    public async Task SendInitializationMessage()
    {
        var profile = _profileService.ActiveProfile
            ?? throw new InvalidOperationException("No active profile");
        var sessionKey = profile.ClaudeSessionKey
            ?? throw new InvalidOperationException("No session key");
        var orgId = await FetchOrganizationId(sessionKey);
        var client = _httpClientFactory.CreateClient("Claude");

        // Create conversation
        var convUrl = new UrlBuilder(Constants.APIEndpoints.ClaudeBase)
            .AppendingPath($"organizations/{orgId}/chat_conversations")
            .Build();

        var convUuid = Guid.NewGuid().ToString("D");
        var convBody = JsonSerializer.Serialize(new { uuid = convUuid, name = "" });
        var convRequest = new HttpRequestMessage(HttpMethod.Post, convUrl);
        convRequest.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        convRequest.Content = new StringContent(convBody, Encoding.UTF8, "application/json");

        var convResponse = await client.SendAsync(convRequest);
        await EnsureSuccessResponse(convResponse, "create conversation");

        var convJson = await convResponse.Content.ReadAsStringAsync();
        var convData = JsonSerializer.Deserialize<ConversationResponse>(convJson);
        var conversationId = convData?.Uuid ?? convUuid;

        // Send "Hi" to Haiku
        var msgUrl = new UrlBuilder(Constants.APIEndpoints.ClaudeBase)
            .AppendingPath($"organizations/{orgId}/chat_conversations/{conversationId}/completion")
            .Build();

        var msgBody = JsonSerializer.Serialize(new
        {
            prompt = "Hi",
            model = Constants.AutoStart.HaikuModel,
            timezone = "UTC"
        });
        var msgRequest = new HttpRequestMessage(HttpMethod.Post, msgUrl);
        msgRequest.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        msgRequest.Content = new StringContent(msgBody, Encoding.UTF8, "application/json");

        var msgResponse = await client.SendAsync(msgRequest);
        await EnsureSuccessResponse(msgResponse, "send initialization message");

        // Delete conversation
        try
        {
            var delUrl = new UrlBuilder(Constants.APIEndpoints.ClaudeBase)
                .AppendingPath($"organizations/{orgId}/chat_conversations/{conversationId}")
                .Build();

            var delRequest = new HttpRequestMessage(HttpMethod.Delete, delUrl);
            delRequest.Headers.Add("Cookie", $"sessionKey={sessionKey}");
            await client.SendAsync(delRequest);
        }
        catch { /* ignore deletion errors */ }

        LoggingService.Instance.Log("Session initialization completed");
    }

    // Console API methods
    public async Task<List<APIOrganization>> FetchConsoleOrganizations(string apiSessionKey)
    {
        var url = new UrlBuilder(Constants.APIEndpoints.ConsoleBase)
            .AppendingPath("organizations")
            .Build();

        var client = _httpClientFactory.CreateClient("Claude");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request);
        await EnsureSuccessResponse(response, "console organizations");

        var json = await response.Content.ReadAsStringAsync();
        var orgs = JsonSerializer.Deserialize<List<ConsoleOrganization>>(json) ?? [];
        return orgs.Select(o => new APIOrganization { Id = o.Uuid, Name = o.Name }).ToList();
    }

    public async Task<APIUsage> FetchAPIUsageData(string organizationId, string apiSessionKey)
    {
        var client = _httpClientFactory.CreateClient("Claude");

        // Fetch spend
        var spendUrl = new UrlBuilder(Constants.APIEndpoints.ConsoleBase)
            .AppendingPath($"organizations/{organizationId}/current_spend")
            .Build();
        var spendReq = new HttpRequestMessage(HttpMethod.Get, spendUrl);
        spendReq.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
        spendReq.Headers.Add("Accept", "application/json");

        // Fetch credits
        var creditsUrl = new UrlBuilder(Constants.APIEndpoints.ConsoleBase)
            .AppendingPath($"organizations/{organizationId}/prepaid/credits")
            .Build();
        var creditsReq = new HttpRequestMessage(HttpMethod.Get, creditsUrl);
        creditsReq.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
        creditsReq.Headers.Add("Accept", "application/json");

        var spendTask = client.SendAsync(spendReq);
        var creditsTask = client.SendAsync(creditsReq);
        await Task.WhenAll(spendTask, creditsTask);

        var spendResponse = await spendTask;
        var creditsResponse = await creditsTask;
        await EnsureSuccessResponse(spendResponse, "current_spend");

        var spendJson = await spendResponse.Content.ReadAsStringAsync();
        var spend = JsonSerializer.Deserialize<CurrentSpendResponse>(spendJson)!;
        var resetsAt = DateTime.TryParse(spend.ResetsAt, out var dt) ? dt : DateTime.UtcNow;

        // Credits endpoint requires billing:view — fail gracefully if permission is missing
        int prepaidCents = 0;
        string currency = "usd";
        try
        {
            await EnsureSuccessResponse(creditsResponse, "prepaid/credits");
            var creditsJson = await creditsResponse.Content.ReadAsStringAsync();
            var credits = JsonSerializer.Deserialize<PrepaidCreditsResponse>(creditsJson)!;
            prepaidCents = credits.Amount;
            currency = credits.Currency;
        }
        catch (HttpRequestException)
        {
            // billing:view permission not available — skip prepaid credits
        }

        return new APIUsage
        {
            CurrentSpendCents = spend.Amount,
            ResetsAt = resetsAt,
            PrepaidCreditsCents = prepaidCents,
            Currency = currency
        };
    }

    public async Task<List<ClaudeCodeUserMetrics>> FetchClaudeCodeAllUsers(
        string organizationUuid, string apiSessionKey)
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfNextMonth = startOfMonth.AddMonths(1);

        var url = new Uri($"{Constants.APIEndpoints.ClaudeCodeMetrics}/users" +
            $"?organization_uuid={Uri.EscapeDataString(organizationUuid)}" +
            $"&start_date={startOfMonth:yyyy-MM-dd}&end_date={startOfNextMonth:yyyy-MM-dd}" +
            $"&limit=200&offset=0&sort_by=total_cost_usd&sort_order=desc");

        var client = _httpClientFactory.CreateClient("Claude");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request);
        await EnsureSuccessResponse(response, "claude_code/metrics_aggs/users (all)");

        var json = await response.Content.ReadAsStringAsync();
        var metricsResponse = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

        return metricsResponse?.Users ?? new List<ClaudeCodeUserMetrics>();
    }

    public async Task<ClaudeCodeUserMetrics?> FetchClaudeCodeUserMetrics(
        string organizationUuid, string apiSessionKey, string search)
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfNextMonth = startOfMonth.AddMonths(1);

        var url = new Uri($"{Constants.APIEndpoints.ClaudeCodeMetrics}/users" +
            $"?organization_uuid={Uri.EscapeDataString(organizationUuid)}" +
            $"&start_date={startOfMonth:yyyy-MM-dd}&end_date={startOfNextMonth:yyyy-MM-dd}" +
            $"&search={Uri.EscapeDataString(search)}&limit=1&offset=0" +
            $"&sort_by=total_cost_usd&sort_order=desc");

        var client = _httpClientFactory.CreateClient("Claude");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request);
        await EnsureSuccessResponse(response, "claude_code/metrics_aggs/users");

        var json = await response.Content.ReadAsStringAsync();
        var metricsResponse = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

        return metricsResponse?.Users.FirstOrDefault();
    }

    private async Task<string> PerformClaudeRequest(string endpoint, string sessionKey)
    {
        var url = new UrlBuilder(Constants.APIEndpoints.ClaudeBase)
            .AppendingPath(endpoint)
            .Build();

        var client = _httpClientFactory.CreateClient("Claude");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("Accept", "application/json");

        LoggingService.Instance.LogAPIRequest(endpoint);

        var response = await client.SendAsync(request);
        LoggingService.Instance.LogAPIResponse(endpoint, (int)response.StatusCode);
        await EnsureSuccessResponse(response, endpoint);

        return await response.Content.ReadAsStringAsync();
    }

    private ClaudeUsage ParseUsageResponse(string data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;

        double sessionPercentage = 0;
        var sessionResetTime = DateTime.UtcNow.AddHours(5);

        if (root.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind == JsonValueKind.Object)
        {
            sessionPercentage = ParseUtilization(fiveHour);
            if (fiveHour.TryGetProperty("resets_at", out var resetsAt) && resetsAt.ValueKind == JsonValueKind.String)
                sessionResetTime = ParseDateTime(resetsAt.GetString()) ?? sessionResetTime;
        }

        double weeklyPercentage = 0;
        var weeklyResetTime = DateTime.UtcNow;

        if (root.TryGetProperty("seven_day", out var sevenDay) && sevenDay.ValueKind == JsonValueKind.Object)
        {
            weeklyPercentage = ParseUtilization(sevenDay);
            if (sevenDay.TryGetProperty("resets_at", out var resetsAt) && resetsAt.ValueKind == JsonValueKind.String)
                weeklyResetTime = ParseDateTime(resetsAt.GetString()) ?? weeklyResetTime;
        }

        double opusPercentage = 0;
        bool hasOpusData = false;
        if (root.TryGetProperty("seven_day_opus", out var sevenDayOpus) && sevenDayOpus.ValueKind == JsonValueKind.Object)
        {
            opusPercentage = ParseUtilization(sevenDayOpus);
            hasOpusData = true;
        }

        double sonnetPercentage = 0;
        DateTime? sonnetResetTime = null;
        bool hasSonnetData = false;
        if (root.TryGetProperty("seven_day_sonnet", out var sevenDaySonnet) && sevenDaySonnet.ValueKind == JsonValueKind.Object)
        {
            sonnetPercentage = ParseUtilization(sevenDaySonnet);
            hasSonnetData = true;
            if (sevenDaySonnet.TryGetProperty("resets_at", out var resetsAt) && resetsAt.ValueKind == JsonValueKind.String)
                sonnetResetTime = ParseDateTime(resetsAt.GetString());
        }

        var weeklyLimit = Constants.WeeklyLimit;

        return new ClaudeUsage
        {
            SessionTokensUsed = 0,
            SessionLimit = 0,
            SessionPercentage = sessionPercentage,
            SessionResetTime = sessionResetTime,
            WeeklyTokensUsed = (int)(weeklyLimit * (weeklyPercentage / 100.0)),
            WeeklyLimit = weeklyLimit,
            WeeklyPercentage = weeklyPercentage,
            WeeklyResetTime = weeklyResetTime,
            OpusWeeklyTokensUsed = (int)(weeklyLimit * (opusPercentage / 100.0)),
            OpusWeeklyPercentage = opusPercentage,
            SonnetWeeklyTokensUsed = (int)(weeklyLimit * (sonnetPercentage / 100.0)),
            SonnetWeeklyPercentage = sonnetPercentage,
            SonnetWeeklyResetTime = sonnetResetTime,
            HasOpusData = hasOpusData,
            HasSonnetData = hasSonnetData,
            LastUpdated = DateTime.UtcNow
        };
    }

    private static double ParseUtilization(JsonElement element)
    {
        if (!element.TryGetProperty("utilization", out var util))
            return 0;

        if (util.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return 0;

        return util.ValueKind switch
        {
            JsonValueKind.Number => util.GetDouble(),
            JsonValueKind.String => double.TryParse(util.GetString()?.Replace("%", ""), out var v) ? v : 0,
            _ => 0
        };
    }

    private static DateTime? ParseDateTime(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        return DateTimeOffset.TryParse(dateStr, out var dto) ? dto.UtcDateTime : null;
    }

    private static async Task EnsureSuccessResponse(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        var preview = body.Length > 200 ? body[..200] : body;

        throw (int)response.StatusCode switch
        {
            401 or 403 => new HttpRequestException($"Unauthorized for {endpoint}. Session key may have expired. {preview}"),
            429 => new HttpRequestException($"Rate limited by Claude API on {endpoint}"),
            >= 500 => new HttpRequestException($"Server error ({(int)response.StatusCode}) on {endpoint}: {preview}"),
            _ => new HttpRequestException($"HTTP {(int)response.StatusCode} on {endpoint}: {preview}")
        };
    }
}
