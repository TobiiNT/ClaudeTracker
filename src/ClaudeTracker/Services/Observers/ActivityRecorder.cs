using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Observers;

/// <summary>
/// Observes ALL hook events and creates ActivityEntry records for the activity feed.
/// Also forwards activity to the session tracking service for per-session history.
/// </summary>
public class ActivityRecorder : IHookEventObserver
{
    private readonly IActivityService _activityService;
    private readonly ISessionTrackingService _sessionTracking;

    public ActivityRecorder(IActivityService activityService, ISessionTrackingService sessionTracking)
    {
        _activityService = activityService;
        _sessionTracking = sessionTracking;
    }

    public void Observe(HookEvent evt)
    {
        var json = TryParse(evt.Payload);
        var sessionId = json?[Fields.SessionId]?.GetValue<string>() ?? "";

        var cwd = json?[Fields.Cwd]?.GetValue<string>() ?? "";
        var projectName = string.IsNullOrEmpty(cwd) ? "" : System.IO.Path.GetFileName(cwd) ?? cwd;

        var entry = new ActivityEntry
        {
            SessionId = sessionId,
            EventName = evt.EventName,
            Timestamp = evt.Timestamp,
            Summary = BuildSummary(evt.EventName, json),
            Detail = BuildDetail(evt.EventName, json),
            Icon = GetIcon(evt.EventName),
            ToolName = GetToolName(evt.EventName, json),
            ProjectName = projectName
        };

        _activityService.Record(entry);
        if (!string.IsNullOrEmpty(sessionId))
            _sessionTracking.RecordActivity(sessionId, entry);
    }

    private static string BuildSummary(string eventName, JsonNode? json) => eventName switch
    {
        Events.PreToolUse or Events.PostToolUse or Events.PostToolUseFailure or Events.PermissionRequest =>
            FormatToolSummary(json?[Fields.ToolName]?.GetValue<string>() ?? "?", json),
        Events.Notification => json?[Fields.Message]?.GetValue<string>() ?? "Notification",
        Events.Stop => "Task completed",
        Events.SessionStart => $"Session started ({json?[Fields.Source]?.GetValue<string>() ?? "?"})",
        Events.SessionEnd => $"Session ended ({json?[Fields.Reason]?.GetValue<string>() ?? "?"})",
        Events.SubagentStart => $"Subagent: {json?[Fields.AgentType]?.GetValue<string>() ?? "?"}",
        Events.SubagentStop => $"Subagent finished: {json?[Fields.AgentType]?.GetValue<string>() ?? "?"}",
        Events.UserPromptSubmit => "User prompt submitted",
        Events.PreCompact or Events.PostCompact => $"Context {(eventName == Events.PreCompact ? "compacting" : "compacted")}",
        Events.WorktreeCreate => $"Worktree: {json?["name"]?.GetValue<string>() ?? "?"}",
        Events.WorktreeRemove => "Worktree removed",
        Events.InstructionsLoaded => $"Loaded: {System.IO.Path.GetFileName(json?[Fields.FilePath]?.GetValue<string>() ?? "?")}",
        Events.ConfigChange => $"Config: {json?[Fields.Source]?.GetValue<string>() ?? "?"}",
        Events.Elicitation => $"MCP: {json?["mcp_server_name"]?.GetValue<string>() ?? "?"} requesting input",
        Events.ElicitationResult => "MCP: user responded",
        Events.TeammateIdle => $"Teammate idle: {json?["teammate_name"]?.GetValue<string>() ?? "?"}",
        Events.TaskCompleted => $"Task done: {json?["task_subject"]?.GetValue<string>() ?? "?"}",
        _ => eventName
    };

    private static string FormatToolSummary(string toolName, JsonNode? json) => toolName switch
    {
        Tools.Bash => FormatBashSummary(json),
        Tools.Edit => $"Edit {Truncate(json?[Fields.ToolInput]?[Fields.FilePath]?.GetValue<string>(), 50)}",
        Tools.Write => $"Write {Truncate(json?[Fields.ToolInput]?[Fields.FilePath]?.GetValue<string>(), 50)}",
        Tools.Read => $"Read {Truncate(json?[Fields.ToolInput]?[Fields.FilePath]?.GetValue<string>(), 50)}",
        Tools.Grep => $"Grep: {Truncate(json?[Fields.ToolInput]?[Fields.Pattern]?.GetValue<string>(), 40)}",
        Tools.Glob => $"Glob: {Truncate(json?[Fields.ToolInput]?[Fields.Pattern]?.GetValue<string>(), 40)}",
        Tools.WebFetch => $"Fetch: {Truncate(json?[Fields.ToolInput]?[Fields.Url]?.GetValue<string>(), 50)}",
        Tools.WebSearch => $"Search: {Truncate(json?[Fields.ToolInput]?[Fields.Query]?.GetValue<string>(), 50)}",
        Tools.AskUserQuestion => "Asking user question",
        _ => toolName.StartsWith("mcp__") ? $"MCP: {toolName}" : toolName
    };

    private static ActivityIcon GetIcon(string eventName) => eventName switch
    {
        Events.PreToolUse or Events.PostToolUse or Events.PostToolUseFailure => ActivityIcon.Tool,
        Events.PermissionRequest => ActivityIcon.Permission,
        Events.SessionStart or Events.SessionEnd => ActivityIcon.Session,
        Events.SubagentStart or Events.SubagentStop => ActivityIcon.Subagent,
        Events.Notification => ActivityIcon.Notification,
        _ => ActivityIcon.System
    };

    private static string? GetToolName(string eventName, JsonNode? json)
    {
        if (eventName is Events.PreToolUse or Events.PostToolUse or Events.PostToolUseFailure or Events.PermissionRequest)
            return json?[Fields.ToolName]?.GetValue<string>();
        return null;
    }

    private static string FormatBashSummary(JsonNode? json)
    {
        var desc = json?[Fields.ToolInput]?[Fields.Description]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(desc))
            return $"Bash \u2013 {Truncate(desc, 55)}";
        return $"Bash: {Truncate(json?[Fields.ToolInput]?[Fields.Command]?.GetValue<string>(), 60)}";
    }

    /// <summary>Returns a detail line (e.g. full command) for entries that have a description summary.</summary>
    private static string? BuildDetail(string eventName, JsonNode? json)
    {
        if (eventName is Events.PreToolUse or Events.PostToolUse or Events.PostToolUseFailure or Events.PermissionRequest)
        {
            var toolName = json?[Fields.ToolName]?.GetValue<string>();
            if (toolName == Tools.Bash)
            {
                var desc = json?[Fields.ToolInput]?[Fields.Description]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(desc))
                    return Truncate(json?[Fields.ToolInput]?[Fields.Command]?.GetValue<string>(), 80);
            }
        }
        return null;
    }

    private static string Truncate(string? s, int max) =>
        s == null ? "?" : s.Length <= max ? s : s[..max] + "...";

    private static JsonNode? TryParse(string payload)
    {
        try { return JsonNode.Parse(payload); } catch { return null; }
    }
}
