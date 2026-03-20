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

    /// <summary>Console window handle from HookBridge (for SetForegroundWindow).</summary>
    [JsonPropertyName("consoleWindowHandle")]
    public long? ConsoleWindowHandle { get; set; }
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
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public ActivityIcon Icon { get; set; }
    public string? ToolName { get; set; }
    public string? Detail { get; set; }
    public string ProjectName { get; set; } = string.Empty;
}

// ── Session State ──

/// <summary>Tracks an active Claude Code session with its activity history.</summary>
public class SessionState
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public string Cwd { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int ToolCallCount { get; set; }
    public int SubagentCount { get; set; }
    public string CurrentActivity { get; set; } = string.Empty;
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
    public long? ConsoleWindowHandle { get; set; }
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

    /// <summary>Original JSON from Claude Code, used to echo back verbatim for updatedPermissions.</summary>
    [JsonIgnore]
    public string? RawJson { get; set; }

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            if (Rules.Count > 0)
            {
                var rule = Rules[0];

                // Bash rules with ruleContent — show "command:*" pattern
                if (!string.IsNullOrEmpty(rule.RuleContent))
                {
                    if (rule.ToolName == "Bash")
                    {
                        var cmd = rule.RuleContent.TrimStart();
                        var firstWord = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cmd;
                        return $"Always allow {firstWord}:*";
                    }
                    var content = rule.RuleContent.Length > 40
                        ? rule.RuleContent[..37] + "..."
                        : rule.RuleContent;
                    return $"Always allow {content}";
                }

                // MCP tools — toolName set but ruleContent empty (e.g., mcp__Database__execute_sql_DataGame)
                if (!string.IsNullOrEmpty(rule.ToolName))
                {
                    return $"Always allow {FormatMcpToolName(rule.ToolName)}";
                }
            }
            if (!string.IsNullOrEmpty(Prefix))
                return $"Always allow {Prefix}";
            if (!string.IsNullOrEmpty(Tool))
                return $"Always allow {Tool}";
            return "Always allow";
        }
    }

    /// <summary>Formats MCP tool names like "mcp__Database__execute_sql_DataGame" → "Database: execute_sql"</summary>
    private static string FormatMcpToolName(string toolName)
    {
        if (!toolName.StartsWith("mcp__"))
            return toolName;

        // mcp__Server__action_Target → ["Server", "action_Target"]
        var parts = toolName[5..].Split("__", 2);
        if (parts.Length == 2)
        {
            var server = parts[0];
            // Strip the target suffix from action (e.g., "execute_sql_DataGame" → "execute_sql")
            var action = parts[1];
            var lastUnderscore = action.LastIndexOf('_');
            if (lastUnderscore > 0)
            {
                // Check if the part after last underscore looks like a target name (starts with uppercase)
                var suffix = action[(lastUnderscore + 1)..];
                if (suffix.Length > 0 && char.IsUpper(suffix[0]))
                    action = action[..lastUnderscore];
            }
            return $"{server}: {action}";
        }
        return parts[0];
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
