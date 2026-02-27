using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

public class NotificationSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("threshold75Enabled")]
    public bool Threshold75Enabled { get; set; } = true;

    [JsonPropertyName("threshold90Enabled")]
    public bool Threshold90Enabled { get; set; } = true;

    [JsonPropertyName("threshold95Enabled")]
    public bool Threshold95Enabled { get; set; } = true;
}
