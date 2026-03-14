using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

/// <summary>Root settings model persisted to settings.json in %APPDATA%/ClaudeTracker.</summary>
public class AppSettings
{
    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();

    [JsonPropertyName("activeProfileId")]
    public Guid? ActiveProfileId { get; set; }

    [JsonPropertyName("appLanguage")]
    public string AppLanguage { get; set; } = "en";

    [JsonPropertyName("launchAtLogin")]
    public bool LaunchAtLogin { get; set; }

    [JsonPropertyName("multiProfileDisplayConfig")]
    public MultiProfileDisplayConfig MultiProfileDisplayConfig { get; set; } = new();

    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; set; } = "single";

    [JsonPropertyName("firstLaunchDate")]
    public DateTime? FirstLaunchDate { get; set; }

    [JsonPropertyName("hasCompletedSetup")]
    public bool HasCompletedSetup { get; set; }

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "auto";

    [JsonPropertyName("isFloatingModeEnabled")]
    public bool IsFloatingModeEnabled { get; set; }

    [JsonPropertyName("floatingWindowLeft")]
    public double? FloatingWindowLeft { get; set; }

    [JsonPropertyName("floatingWindowTop")]
    public double? FloatingWindowTop { get; set; }

    [JsonPropertyName("isFloatingWidgetDocked")]
    public bool IsFloatingWidgetDocked { get; set; }

    [JsonPropertyName("hasStarredGitHub")]
    public bool HasStarredGitHub { get; set; }

    [JsonPropertyName("lastStarPromptDate")]
    public DateTime? LastStarPromptDate { get; set; }

    [JsonPropertyName("starPromptDismissedForever")]
    public bool StarPromptDismissedForever { get; set; }

    [JsonPropertyName("popoverTimeDisplay")]
    public string PopoverTimeDisplay { get; set; } = "remainingTime";

    [JsonPropertyName("timeFormatPreference")]
    public string TimeFormatPreference { get; set; } = "system";
}
