using System.IO;
using System.Collections.Generic;

namespace ClaudeTracker.Utilities;

/// <summary>Application-wide constants: API endpoints, intervals, thresholds, and paths.</summary>
public static class Constants
{
    public static class APIEndpoints
    {
        public const string ClaudeBase = "https://claude.ai/api";
        public const string ConsoleBase = "https://console.anthropic.com/api";
        public const string OAuthUsage = "https://api.anthropic.com/api/oauth/usage";
    }

    public static class RefreshIntervals
    {
        public const double DefaultSeconds = 60;
        public const double MinSeconds = 10;
        public const double MaxSeconds = 300;
    }

    public const double SessionWindowHours = 5.0;
    public const int WeeklyLimit = 1_000_000;

    public static class NotificationThresholds
    {
        public const double Warning = 75.0;
        public const double High = 90.0;
        public const double Critical = 95.0;
    }

    public static class GitHub
    {
        public const string Owner = "TobiiNT";
        public const string Repo = "ClaudeTracker";
        public const string RepoUrl = "https://github.com/TobiiNT/ClaudeTracker";
    }

    public static class Feedback
    {
        // Google Forms integration — fill in after creating your form
        public const string GoogleFormId = "1FAIpQLScq3wQ3hgs6JnrlgRuZergFbhrBEk2j81wBGJb4OC9S-gAEjA";
        public const string EntryRating = "entry.351097210";
        public const string EntryComment = "entry.2038771915";
        public const string EntryVersion = "entry.752645992";

        public static bool IsConfigured => !string.IsNullOrEmpty(GoogleFormId);
        public static string SubmitUrl => $"https://docs.google.com/forms/d/e/{GoogleFormId}/formResponse";

        public const double PromptAfterDays = 7.0;
        public const double RemindIntervalDays = 14.0;
    }

    public static class UITiming
    {
        public const double PopoverCloseDelayMs = 150;
        public const double RefreshAnimationMs = 1000;
        public const double HoverAnimationMs = 200;
        public const double TransitionMs = 300;
    }

    public static class WindowSizes
    {
        public const double SettingsWidth = 720;
        public const double SettingsHeight = 580;
        public const double PopoverWidth = 340;
        public const double PopoverMaxHeight = 600;
        public const double FloatingWidth = 280;
        public const double FloatingHeight = 180;
    }

    public static class CredentialManager
    {
        public const string ClaudeCodeTarget = "Claude Code-credentials";
        public const string AppName = "ClaudeTracker";
    }

    public static class AutoStart
    {
        public const double CheckIntervalMinutes = 5.0;
        public const string HaikuModel = "claude-haiku-4-5-20251001";
    }

    public static class StatusAPI
    {
        public const string StatusUrl = "https://status.claude.com/api/v2/status.json";
        public const double RefreshIntervalMinutes = 5.0;
    }

    public static class Hooks
    {
        public static string PipeName => $"ClaudeTracker-Hooks-{Environment.UserName}";
        public const int MaxConcurrentConnections = 10;
        public const int MaxMessageSize = 5 * 1024 * 1024; // 5 MB
        public const int ConnectionTimeoutMs = 3000;
        public const int ResponseTimeoutMs = 310_000; // Above Claude's 300s permission timeout
        public const int StaleSessionMinutes = 15;
        public const int DefaultMaxActivityEntries = 200;
        public const int DefaultMaxFeedEntries = 10;

        public static string ClaudeSettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        public static readonly string[] AllEvents =
        {
            "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionRequest", "Notification", "Stop",
            "SessionStart", "SessionEnd", "UserPromptSubmit",
            "SubagentStart", "SubagentStop",
            "PreCompact", "PostCompact",
            "WorktreeCreate", "WorktreeRemove",
            "InstructionsLoaded", "ConfigChange",
            "Elicitation", "ElicitationResult",
            "TeammateIdle", "TaskCompleted"
        };

        public static readonly HashSet<string> AsyncEvents = new()
        {
            "PostToolUse", "PostToolUseFailure",
            "SessionStart", "SessionEnd",
            "SubagentStart", "InstructionsLoaded",
            "PreCompact", "PostCompact",
            "WorktreeRemove", "ElicitationResult"
        };

        public static readonly HashSet<string> InteractiveEvents = new()
        {
            "PermissionRequest", "PreToolUse",
            "Elicitation", "UserPromptSubmit",
            "Stop", "SubagentStop", "ConfigChange"
        };
    }

    public static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTracker");

    public static string SettingsFilePath =>
        Path.Combine(AppDataPath, "settings.json");

    public static string LogFilePath =>
        Path.Combine(AppDataPath, "logs", "claudetracker-.log");

    public const string DisplayName = "Claude Tracker";

    public const string MutexName = "ClaudeTracker-SingleInstance";

    public static string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "dev";
}
