using System.Globalization;
using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

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

public class APIOrganization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;
}
