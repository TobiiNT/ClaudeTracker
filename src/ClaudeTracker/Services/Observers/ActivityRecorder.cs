using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

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
        var sessionId = json?["session_id"]?.GetValue<string>() ?? "";

        var entry = new ActivityEntry
        {
            SessionId = sessionId,
            EventName = evt.EventName,
            Timestamp = evt.Timestamp,
            Summary = BuildSummary(evt.EventName, json),
            Icon = GetIcon(evt.EventName),
            ToolName = GetToolName(evt.EventName, json),
            RawPayload = evt.Payload
        };

        _activityService.Record(entry);
        if (!string.IsNullOrEmpty(sessionId))
            _sessionTracking.RecordActivity(sessionId, entry);
    }

    private static string BuildSummary(string eventName, JsonNode? json) => eventName switch
    {
        "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionRequest" =>
            FormatToolSummary(json?["tool_name"]?.GetValue<string>() ?? "?", json),
        "Notification" => json?["message"]?.GetValue<string>() ?? "Notification",
        "Stop" => "Task completed",
        "SessionStart" => $"Session started ({json?["source"]?.GetValue<string>() ?? "?"})",
        "SessionEnd" => $"Session ended ({json?["reason"]?.GetValue<string>() ?? "?"})",
        "SubagentStart" => $"Subagent: {json?["agent_type"]?.GetValue<string>() ?? "?"}",
        "SubagentStop" => $"Subagent finished: {json?["agent_type"]?.GetValue<string>() ?? "?"}",
        "UserPromptSubmit" => "User prompt submitted",
        "PreCompact" or "PostCompact" => $"Context {(eventName == "PreCompact" ? "compacting" : "compacted")}",
        "WorktreeCreate" => $"Worktree: {json?["name"]?.GetValue<string>() ?? "?"}",
        "WorktreeRemove" => "Worktree removed",
        "InstructionsLoaded" => $"Loaded: {System.IO.Path.GetFileName(json?["file_path"]?.GetValue<string>() ?? "?")}",
        "ConfigChange" => $"Config: {json?["source"]?.GetValue<string>() ?? "?"}",
        "Elicitation" => $"MCP: {json?["mcp_server_name"]?.GetValue<string>() ?? "?"} requesting input",
        "ElicitationResult" => "MCP: user responded",
        "TeammateIdle" => $"Teammate idle: {json?["teammate_name"]?.GetValue<string>() ?? "?"}",
        "TaskCompleted" => $"Task done: {json?["task_subject"]?.GetValue<string>() ?? "?"}",
        _ => eventName
    };

    private static string FormatToolSummary(string toolName, JsonNode? json) => toolName switch
    {
        "Bash" => $"Bash: {Truncate(json?["tool_input"]?["command"]?.GetValue<string>(), 60)}",
        "Edit" => $"Edit {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Write" => $"Write {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Read" => $"Read {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Grep" => $"Grep: {Truncate(json?["tool_input"]?["pattern"]?.GetValue<string>(), 40)}",
        "Glob" => $"Glob: {Truncate(json?["tool_input"]?["pattern"]?.GetValue<string>(), 40)}",
        "WebFetch" => $"Fetch: {Truncate(json?["tool_input"]?["url"]?.GetValue<string>(), 50)}",
        "WebSearch" => $"Search: {Truncate(json?["tool_input"]?["query"]?.GetValue<string>(), 50)}",
        "AskUserQuestion" => "Asking user question",
        _ => toolName.StartsWith("mcp__") ? $"MCP: {toolName}" : toolName
    };

    private static ActivityIcon GetIcon(string eventName) => eventName switch
    {
        "PreToolUse" or "PostToolUse" or "PostToolUseFailure" => ActivityIcon.Tool,
        "PermissionRequest" => ActivityIcon.Permission,
        "SessionStart" or "SessionEnd" => ActivityIcon.Session,
        "SubagentStart" or "SubagentStop" => ActivityIcon.Subagent,
        "Notification" => ActivityIcon.Notification,
        _ => ActivityIcon.System
    };

    private static string? GetToolName(string eventName, JsonNode? json)
    {
        if (eventName is "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionRequest")
            return json?["tool_name"]?.GetValue<string>();
        return null;
    }

    private static string Truncate(string? s, int max) =>
        s == null ? "?" : s.Length <= max ? s : s[..max] + "...";

    private static JsonNode? TryParse(string payload)
    {
        try { return JsonNode.Parse(payload); } catch { return null; }
    }
}
