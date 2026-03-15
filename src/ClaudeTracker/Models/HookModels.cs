using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

// ── IPC Envelope ──

/// <summary>Generic IPC message envelope for hook events received from Claude Code.</summary>
public class HookEvent
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>IPC response sent back to Claude Code for interactive hook events.</summary>
public class HookResponse
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("jsonOutput")]
    public string? JsonOutput { get; set; }
}

// ── Activity Feed ──

/// <summary>Icon category for activity feed entries.</summary>
public enum ActivityIcon
{
    Tool,
    Permission,
    Session,
    Subagent,
    System,
    Notification
}

/// <summary>Universal log record for the activity feed.</summary>
public class ActivityEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public ActivityIcon Icon { get; set; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("rawPayload")]
    public string RawPayload { get; set; } = string.Empty;
}

// ── Session State ──

/// <summary>Tracks an active Claude Code session with its activity history.</summary>
public class SessionState
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("toolCallCount")]
    public int ToolCallCount { get; set; }

    [JsonPropertyName("subagentCount")]
    public int SubagentCount { get; set; }

    [JsonPropertyName("currentActivity")]
    public string CurrentActivity { get; set; } = string.Empty;

    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("activeSubagents")]
    public List<string> ActiveSubagents { get; set; } = new();

    [JsonIgnore]
    public ObservableCollection<ActivityEntry> Activities { get; set; } = new();

    [JsonIgnore]
    public string ProjectName => string.IsNullOrEmpty(Cwd)
        ? "Unknown"
        : System.IO.Path.GetFileName(Cwd) ?? Cwd;
}

// ── Permission Request ──

/// <summary>Information about a permission request from Claude Code.</summary>
public class PermissionRequestInfo
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("toolInput")]
    public Dictionary<string, object> ToolInput { get; set; } = new();

    [JsonPropertyName("permissionSuggestions")]
    public List<PermissionSuggestion> PermissionSuggestions { get; set; } = new();

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("permissionMode")]
    public string PermissionMode { get; set; } = string.Empty;
}

/// <summary>A suggested permission rule that can be applied.</summary>
public class PermissionSuggestion
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = string.Empty;

    [JsonPropertyName("destination")]
    public string Destination { get; set; } = string.Empty;

    [JsonPropertyName("rules")]
    public List<PermissionRule> Rules { get; set; } = new();

    [JsonPropertyName("directories")]
    public List<string> Directories { get; set; } = new();

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            // For Bash rules, extract the command name and show as "command:*" pattern
            if (Rules.Count > 0 && !string.IsNullOrEmpty(Rules[0].RuleContent))
            {
                var rule = Rules[0];
                if (rule.ToolName == "Bash")
                {
                    var cmd = rule.RuleContent.TrimStart();
                    var firstWord = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cmd;
                    return $"Always allow {firstWord}:*";
                }
                // Truncate long rule content
                var content = rule.RuleContent.Length > 40
                    ? rule.RuleContent[..37] + "..."
                    : rule.RuleContent;
                return $"Always allow {content}";
            }
            if (!string.IsNullOrEmpty(Prefix))
                return $"Always allow {Prefix}";
            if (!string.IsNullOrEmpty(Tool))
                return $"Always allow {Tool}";
            return "Always allow";
        }
    }
}

/// <summary>A single permission rule entry.</summary>
public class PermissionRule
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("ruleContent")]
    public string RuleContent { get; set; } = string.Empty;
}

/// <summary>User's decision on a permission request.</summary>
public enum PermissionDecision
{
    Allow,
    Deny,
    HandleInTerminal,
    AlwaysAllow
}

/// <summary>Result of the user's permission decision, including any applied suggestion.</summary>
public class PermissionDecisionResult
{
    [JsonPropertyName("decision")]
    public PermissionDecision Decision { get; set; }

    [JsonPropertyName("appliedSuggestion")]
    public PermissionSuggestion? AppliedSuggestion { get; set; }

    [JsonPropertyName("updatedInput")]
    public Dictionary<string, object>? UpdatedInput { get; set; }
}

// ── PreToolUse ──

/// <summary>Information about a tool use before execution.</summary>
public class PreToolUseInfo
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("toolInput")]
    public Dictionary<string, object> ToolInput { get; set; } = new();

    [JsonPropertyName("toolUseId")]
    public string ToolUseId { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;
}

/// <summary>Result returned for PreToolUse events.</summary>
public class PreToolUseResult
{
    [JsonPropertyName("permissionDecision")]
    public string PermissionDecision { get; set; } = string.Empty;

    [JsonPropertyName("updatedInput")]
    public Dictionary<string, object>? UpdatedInput { get; set; }

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; set; }
}

// ── Elicitation ──

/// <summary>Information about an elicitation request from an MCP server.</summary>
public class ElicitationInfo
{
    [JsonPropertyName("mcpServerName")]
    public string McpServerName { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("elicitationId")]
    public string ElicitationId { get; set; } = string.Empty;

    [JsonPropertyName("requestedSchema")]
    public Dictionary<string, object> RequestedSchema { get; set; } = new();

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;
}

/// <summary>Result of the user's elicitation decision.</summary>
public class ElicitationDecisionResult
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public Dictionary<string, object> Content { get; set; } = new();
}

// ── Other Event Info Models ──

/// <summary>Information about a user prompt submission.</summary>
public class UserPromptSubmitInfo
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>Information about a stop event.</summary>
public class StopInfo
{
    [JsonPropertyName("lastAssistantMessage")]
    public string LastAssistantMessage { get; set; } = string.Empty;

    [JsonPropertyName("stopHookActive")]
    public bool StopHookActive { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;
}

/// <summary>Information about a subagent stop event.</summary>
public class SubagentStopInfo
{
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("agentType")]
    public string AgentType { get; set; } = string.Empty;

    [JsonPropertyName("lastAssistantMessage")]
    public string LastAssistantMessage { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>Information about a configuration change event.</summary>
public class ConfigChangeInfo
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

// ── Interactive Event Args ──

/// <summary>Generic EventArgs for interactive hook events that require a user response.</summary>
/// <typeparam name="T">The type of info payload for this event.</typeparam>
public class HookInteractiveEventArgs<T> : EventArgs
{
    public T Info { get; }
    public TaskCompletionSource<HookResponse> ResponseSource { get; }

    public HookInteractiveEventArgs(T info, TaskCompletionSource<HookResponse> responseSource)
    {
        Info = info;
        ResponseSource = responseSource;
    }
}
