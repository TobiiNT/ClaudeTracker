using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

public class AccountInfo
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();
}

public class OverageSpendLimitResponse
{
    [JsonPropertyName("monthly_credit_limit")]
    public double? MonthlyCreditLimit { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool? IsEnabled { get; set; }
}

public class CurrentSpendResponse
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("resets_at")]
    public string ResetsAt { get; set; } = string.Empty;
}

public class PrepaidCreditsResponse
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("auto_reload_settings")]
    public AutoReloadSettings? AutoReloadSettings { get; set; }
}

public class AutoReloadSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("threshold")]
    public int? Threshold { get; set; }

    [JsonPropertyName("reload_amount")]
    public int? ReloadAmount { get; set; }
}

public class OrganizationRateLimitsResponse
{
    [JsonPropertyName("rate_limit_tier")]
    public string RateLimitTier { get; set; } = string.Empty;

    /// <summary>Monthly spend threshold in cents (e.g. 20000000 = $200,000).</summary>
    [JsonPropertyName("spend_threshold")]
    public int SpendThreshold { get; set; }
}

public class ConsoleOrganization
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class ConversationResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;
}

public class CliCredentialsJson
{
    [JsonPropertyName("claudeAiOauth")]
    public CliOAuthData? ClaudeAiOauth { get; set; }

    [JsonPropertyName("organizationUuid")]
    public string? OrganizationUuid { get; set; }
}

public class CliOAuthData
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>Unix timestamp in milliseconds</summary>
    [JsonPropertyName("expiresAt")]
    public long? ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("subscriptionType")]
    public string? SubscriptionType { get; set; }
}
