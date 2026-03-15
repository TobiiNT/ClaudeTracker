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

    [JsonPropertyName("hasSentFeedback")]
    public bool HasSentFeedback { get; set; }

    [JsonPropertyName("lastFeedbackPromptDate")]
    public DateTime? LastFeedbackPromptDate { get; set; }

    [JsonPropertyName("feedbackPromptDismissedForever")]
    public bool FeedbackPromptDismissedForever { get; set; }

    [JsonPropertyName("feedbackRating")]
    public int? FeedbackRating { get; set; }

    [JsonPropertyName("popoverTimeDisplay")]
    public PopoverTimeDisplay PopoverTimeDisplay { get; set; } = PopoverTimeDisplay.RemainingTime;

    [JsonPropertyName("timeFormatPreference")]
    public TimeFormatPreference TimeFormatPreference { get; set; } = TimeFormatPreference.System;

    // ── Hooks Integration ──

    [JsonPropertyName("hooksOnboardingSeen")]
    public bool HooksOnboardingSeen { get; set; }

    [JsonPropertyName("hooksOnboardingDismissed")]
    public bool HooksOnboardingDismissed { get; set; }

    [JsonPropertyName("hooksEnabled")]
    public bool HooksEnabled { get; set; }

    [JsonPropertyName("hookPermissionPopupsEnabled")]
    public bool HookPermissionPopupsEnabled { get; set; } = true;

    [JsonPropertyName("hookElicitationPopupsEnabled")]
    public bool HookElicitationPopupsEnabled { get; set; } = true;

    [JsonPropertyName("hookActivityFeedEnabled")]
    public bool HookActivityFeedEnabled { get; set; } = true;

    [JsonPropertyName("hookMaxFeedEntries")]
    public int HookMaxFeedEntries { get; set; } = 10;

    [JsonPropertyName("hookNotificationPreferences")]
    public Dictionary<string, bool> HookNotificationPreferences { get; set; } = new()
    {
        ["stop"] = true,
        ["toolError"] = true,
        ["permission"] = true,
        ["idle"] = true,
        ["configChange"] = false,
        ["sessionLifecycle"] = false,
        ["subagent"] = false
    };
}
