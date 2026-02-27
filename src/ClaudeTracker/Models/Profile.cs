using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

public class Profile
{
    // Identity
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // Credentials (stored directly in profile)
    [JsonPropertyName("claudeSessionKey")]
    public string? ClaudeSessionKey { get; set; }

    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("apiSessionKey")]
    public string? ApiSessionKey { get; set; }

    [JsonPropertyName("apiOrganizationId")]
    public string? ApiOrganizationId { get; set; }

    [JsonPropertyName("cliCredentialsJSON")]
    public string? CliCredentialsJSON { get; set; }

    // CLI Account Sync Metadata
    [JsonPropertyName("hasCliAccount")]
    public bool HasCliAccount { get; set; }

    [JsonPropertyName("cliAccountSyncedAt")]
    public DateTime? CliAccountSyncedAt { get; set; }

    // Usage Data (Per-Profile)
    [JsonPropertyName("claudeUsage")]
    public ClaudeUsage? ClaudeUsage { get; set; }

    [JsonPropertyName("apiUsage")]
    public APIUsage? ApiUsage { get; set; }

    // Appearance Settings (Per-Profile)
    [JsonPropertyName("iconConfig")]
    public MenuBarIconConfiguration IconConfig { get; set; } = MenuBarIconConfiguration.Default;

    // Behavior Settings (Per-Profile)
    [JsonPropertyName("refreshInterval")]
    public double RefreshInterval { get; set; } = 30.0;

    [JsonPropertyName("autoStartSessionEnabled")]
    public bool AutoStartSessionEnabled { get; set; }

    [JsonPropertyName("checkOverageLimitEnabled")]
    public bool CheckOverageLimitEnabled { get; set; } = true;

    // Notification Settings (Per-Profile)
    [JsonPropertyName("notificationSettings")]
    public NotificationSettings NotificationSettings { get; set; } = new();

    // Display Configuration
    [JsonPropertyName("isSelectedForDisplay")]
    public bool IsSelectedForDisplay { get; set; } = true;

    // Metadata
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastUsedAt")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    // Computed Properties
    [JsonIgnore]
    public bool HasClaudeAI => !string.IsNullOrEmpty(ClaudeSessionKey) && !string.IsNullOrEmpty(OrganizationId);

    [JsonIgnore]
    public bool HasAPIConsole => !string.IsNullOrEmpty(ApiSessionKey) && !string.IsNullOrEmpty(ApiOrganizationId);

    [JsonIgnore]
    public bool HasAnyCredentials => HasClaudeAI || HasAPIConsole || !string.IsNullOrEmpty(CliCredentialsJSON);

    [JsonIgnore]
    public bool HasUsageCredentials => HasClaudeAI || HasAPIConsole || !string.IsNullOrEmpty(CliCredentialsJSON);
}

public class ProfileCredentials
{
    public string? ClaudeSessionKey { get; set; }
    public string? OrganizationId { get; set; }
    public string? ApiSessionKey { get; set; }
    public string? ApiOrganizationId { get; set; }
    public string? CliCredentialsJSON { get; set; }

    public bool HasClaudeAI => !string.IsNullOrEmpty(ClaudeSessionKey) && !string.IsNullOrEmpty(OrganizationId);
    public bool HasAPIConsole => !string.IsNullOrEmpty(ApiSessionKey) && !string.IsNullOrEmpty(ApiOrganizationId);
    public bool HasCLI => !string.IsNullOrEmpty(CliCredentialsJSON);
}
