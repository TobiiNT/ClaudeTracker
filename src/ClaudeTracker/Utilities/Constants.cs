using System.IO;
using System.Collections.Generic;

namespace ClaudeTracker.Utilities;

/// <summary>Application-wide constants: API endpoints, intervals, thresholds, and paths.</summary>
public static class Constants
{
    public static class APIEndpoints
    {
        public const string ClaudeBase = "https://claude.ai/api";
        public const string PlatformBase = "https://platform.claude.com";
        public const string PlatformLogin = PlatformBase + "/login";
        public const string ConsoleBase = PlatformBase + "/api";
        public const string ClaudeCodeMetrics = ConsoleBase + "/claude_code/metrics_aggs";
        public const string OAuthUsage = "https://api.anthropic.com/api/oauth/usage";
        public const string OAuthTokenEndpoint = "https://platform.claude.com/api/oauth/token";
        public const string OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
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

    public static class WebView2
    {
        public static string ProfilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTracker", "WebView2Profile");
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

        // ── Hook Event Names ──
        public static class Events
        {
            public const string PreToolUse = "PreToolUse";
            public const string PostToolUse = "PostToolUse";
            public const string PostToolUseFailure = "PostToolUseFailure";
            public const string PermissionRequest = "PermissionRequest";
            public const string Notification = "Notification";
            public const string Stop = "Stop";
            public const string SessionStart = "SessionStart";
            public const string SessionEnd = "SessionEnd";
            public const string UserPromptSubmit = "UserPromptSubmit";
            public const string SubagentStart = "SubagentStart";
            public const string SubagentStop = "SubagentStop";
            public const string PreCompact = "PreCompact";
            public const string PostCompact = "PostCompact";
            public const string WorktreeCreate = "WorktreeCreate";
            public const string WorktreeRemove = "WorktreeRemove";
            public const string InstructionsLoaded = "InstructionsLoaded";
            public const string ConfigChange = "ConfigChange";
            public const string Elicitation = "Elicitation";
            public const string ElicitationResult = "ElicitationResult";
            public const string TeammateIdle = "TeammateIdle";
            public const string TaskCompleted = "TaskCompleted";
        }

        // ── Claude Code Tool Names ──
        public static class Tools
        {
            public const string Bash = "Bash";
            public const string Read = "Read";
            public const string Edit = "Edit";
            public const string Write = "Write";
            public const string Glob = "Glob";
            public const string Grep = "Grep";
            public const string WebFetch = "WebFetch";
            public const string WebSearch = "WebSearch";
            public const string AskUserQuestion = "AskUserQuestion";
        }

        // ── Claude Code JSON Payload Fields ──
        public static class Fields
        {
            public const string HookEventName = "hook_event_name";
            public const string ToolName = "tool_name";
            public const string ToolInput = "tool_input";
            public const string SessionId = "session_id";
            public const string Cwd = "cwd";
            public const string Command = "command";
            public const string Description = "description";
            public const string FilePath = "file_path";
            public const string Pattern = "pattern";
            public const string Query = "query";
            public const string Url = "url";
            public const string Question = "question";
            public const string Questions = "questions";
            public const string Answers = "answers";
            public const string PermissionSuggestions = "permission_suggestions";
            public const string PermissionMode = "permission_mode";
            public const string Source = "source";
            public const string Reason = "reason";
            public const string Message = "message";
            public const string AgentType = "agent_type";
            public const string Model = "model";
            public const string AgentId = "agent_id";
            public const string OldString = "old_string";
            public const string NewString = "new_string";
            public const string Content = "content";
            public const string Prompt = "prompt";
        }

        // ── Hook Response Fields ──
        public static class Response
        {
            public const string HookSpecificOutput = "hookSpecificOutput";
            public const string HookEventName = "hookEventName";
            public const string Decision = "decision";
            public const string Behavior = "behavior";
            public const string UpdatedInput = "updatedInput";
            public const string UpdatedPermissions = "updatedPermissions";
            public const string Allow = "allow";
            public const string Deny = "deny";
        }

        public static readonly string[] AllEvents =
        {
            Events.PreToolUse, Events.PostToolUse, Events.PostToolUseFailure,
            Events.PermissionRequest, Events.Notification, Events.Stop,
            Events.SessionStart, Events.SessionEnd, Events.UserPromptSubmit,
            Events.SubagentStart, Events.SubagentStop,
            Events.PreCompact, Events.PostCompact,
            Events.WorktreeCreate, Events.WorktreeRemove,
            Events.InstructionsLoaded, Events.ConfigChange,
            Events.Elicitation, Events.ElicitationResult,
            Events.TeammateIdle, Events.TaskCompleted
        };

        public static readonly HashSet<string> AsyncEvents = new()
        {
            Events.PostToolUse, Events.PostToolUseFailure,
            Events.SessionStart, Events.SessionEnd,
            Events.SubagentStart, Events.InstructionsLoaded,
            Events.PreCompact, Events.PostCompact,
            Events.WorktreeRemove, Events.ElicitationResult
        };

        public static readonly HashSet<string> InteractiveEvents = new()
        {
            Events.PermissionRequest, Events.PreToolUse,
            Events.Elicitation, Events.UserPromptSubmit,
            Events.Stop, Events.SubagentStop, Events.ConfigChange
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
