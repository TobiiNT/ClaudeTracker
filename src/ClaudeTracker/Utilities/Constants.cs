using System.IO;

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
        public const double DefaultSeconds = 30;
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
