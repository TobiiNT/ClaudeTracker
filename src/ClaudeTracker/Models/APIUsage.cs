using System.Globalization;
using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

/// <summary>Anthropic Console API billing data: spend, credits, and formatted amounts.</summary>
public class APIUsage
{
    [JsonPropertyName("currentSpendCents")]
    public int CurrentSpendCents { get; set; }

    [JsonPropertyName("resetsAt")]
    public DateTime ResetsAt { get; set; }

    [JsonPropertyName("prepaidCreditsCents")]
    public int PrepaidCreditsCents { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonIgnore]
    public double UsedAmount => CurrentSpendCents / 100.0;

    [JsonIgnore]
    public double RemainingAmount => PrepaidCreditsCents / 100.0;

    [JsonIgnore]
    public double TotalCredits => UsedAmount + RemainingAmount;

    [JsonIgnore]
    public double UsagePercentage => TotalCredits > 0 ? (UsedAmount / TotalCredits) * 100.0 : 0;

    [JsonIgnore]
    public string FormattedUsed => FormatCurrency(UsedAmount);

    [JsonIgnore]
    public string FormattedRemaining => FormatCurrency(RemainingAmount);

    [JsonIgnore]
    public string FormattedTotal => FormatCurrency(TotalCredits);

    private string FormatCurrency(double amount)
    {
        try
        {
            var culture = CultureInfo.CurrentCulture;
            return amount.ToString("C2", culture);
        }
        catch
        {
            return $"{Currency} {amount:F2}";
        }
    }
}

/// <summary>Per-user Claude Code metrics from platform.claude.com analytics API.</summary>
public class ClaudeCodeUserMetrics
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("api_key_name")]
    public string? ApiKeyName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("avg_cost_per_day")]
    public string AvgCostPerDay { get; set; } = "0";

    [JsonPropertyName("avg_lines_accepted_per_day")]
    public int AvgLinesAcceptedPerDay { get; set; }

    [JsonPropertyName("total_cost")]
    public string TotalCost { get; set; } = "0";

    [JsonPropertyName("total_lines_accepted")]
    public int TotalLinesAccepted { get; set; }

    [JsonPropertyName("total_sessions")]
    public int TotalSessions { get; set; }

    [JsonPropertyName("last_active")]
    public string? LastActive { get; set; }

    [JsonPropertyName("prs_with_cc")]
    public int PrsWithCc { get; set; }

    [JsonPropertyName("total_prs")]
    public int TotalPrs { get; set; }

    [JsonPropertyName("prs_with_cc_percentage")]
    public double PrsWithCcPercentage { get; set; }

    // Computed
    [JsonIgnore]
    public double TotalCostUsd => double.TryParse(TotalCost, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var v) ? v : 0;

    [JsonIgnore]
    public double AvgCostPerDayUsd => double.TryParse(AvgCostPerDay, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var v) ? v : 0;

    [JsonIgnore]
    public string DisplayName => Email ?? ApiKeyName ?? "Unknown";

    [JsonIgnore]
    public string FormattedTotalCost => $"${TotalCostUsd:F2}";

    [JsonIgnore]
    public string FormattedAvgCostPerDay => $"${AvgCostPerDayUsd:F2}/day";
}

/// <summary>Response wrapper for Claude Code user metrics API.</summary>
public class ClaudeCodeMetricsResponse
{
    [JsonPropertyName("organization_id")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("total_users")]
    public int TotalUsers { get; set; }

    [JsonPropertyName("users")]
    public List<ClaudeCodeUserMetrics> Users { get; set; } = new();
}

/// <summary>An Anthropic Console organization with ID and display name.</summary>
public class APIOrganization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;
}
