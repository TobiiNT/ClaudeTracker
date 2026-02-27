using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

public class ClaudeUsage
{
    // Session data (5-hour rolling window)
    [JsonPropertyName("sessionTokensUsed")]
    public int SessionTokensUsed { get; set; }

    [JsonPropertyName("sessionLimit")]
    public int SessionLimit { get; set; }

    [JsonPropertyName("sessionPercentage")]
    public double SessionPercentage { get; set; }

    [JsonPropertyName("sessionResetTime")]
    public DateTime SessionResetTime { get; set; }

    // Weekly data (all models)
    [JsonPropertyName("weeklyTokensUsed")]
    public int WeeklyTokensUsed { get; set; }

    [JsonPropertyName("weeklyLimit")]
    public int WeeklyLimit { get; set; }

    [JsonPropertyName("weeklyPercentage")]
    public double WeeklyPercentage { get; set; }

    [JsonPropertyName("weeklyResetTime")]
    public DateTime WeeklyResetTime { get; set; }

    // Weekly data (Opus only)
    [JsonPropertyName("opusWeeklyTokensUsed")]
    public int OpusWeeklyTokensUsed { get; set; }

    [JsonPropertyName("opusWeeklyPercentage")]
    public double OpusWeeklyPercentage { get; set; }

    // Weekly data (Sonnet only)
    [JsonPropertyName("sonnetWeeklyTokensUsed")]
    public int SonnetWeeklyTokensUsed { get; set; }

    [JsonPropertyName("sonnetWeeklyPercentage")]
    public double SonnetWeeklyPercentage { get; set; }

    [JsonPropertyName("sonnetWeeklyResetTime")]
    public DateTime? SonnetWeeklyResetTime { get; set; }

    // Extra usage data
    [JsonPropertyName("costUsed")]
    public double? CostUsed { get; set; }

    [JsonPropertyName("costLimit")]
    public double? CostLimit { get; set; }

    [JsonPropertyName("costCurrency")]
    public string? CostCurrency { get; set; }

    // Metadata
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; }

    // Whether the API returned per-model data (not null)
    [JsonPropertyName("hasOpusData")]
    public bool HasOpusData { get; set; }

    [JsonPropertyName("hasSonnetData")]
    public bool HasSonnetData { get; set; }

    [JsonIgnore]
    public double RemainingPercentage => Math.Max(0, 100 - SessionPercentage);

    public static ClaudeUsage Empty => new()
    {
        SessionTokensUsed = 0,
        SessionLimit = 0,
        SessionPercentage = 0,
        SessionResetTime = DateTime.UtcNow.AddHours(5),
        WeeklyTokensUsed = 0,
        WeeklyLimit = Utilities.Constants.WeeklyLimit,
        WeeklyPercentage = 0,
        WeeklyResetTime = GetNextMondayNoon(),
        OpusWeeklyTokensUsed = 0,
        OpusWeeklyPercentage = 0,
        SonnetWeeklyTokensUsed = 0,
        SonnetWeeklyPercentage = 0,
        SonnetWeeklyResetTime = null,
        CostUsed = null,
        CostLimit = null,
        CostCurrency = null,
        LastUpdated = DateTime.UtcNow
    };

    private static DateTime GetNextMondayNoon()
    {
        var now = DateTime.UtcNow;
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return now.Date.AddDays(daysUntilMonday).AddHours(12).AddMinutes(59);
    }
}
