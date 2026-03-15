using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles PermissionRequest events from Claude Code.
/// Parses the permission request payload, raises an event for the UI popup,
/// and awaits the user's decision before returning the response to Claude Code.
/// </summary>
public class PermissionRequestHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;

    /// <summary>Raised when a permission request requires user interaction.</summary>
    public event EventHandler<HookInteractiveEventArgs<PermissionRequestInfo>>? PermissionRequested;

    public PermissionRequestHandler(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool CanHandle(string eventName) => eventName == Events.PermissionRequest;

    public async Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"PermissionRequestHandler: Processing request {evt.RequestId}");

        // If permission popups are disabled, return success with no jsonOutput
        // (falls back to terminal handling)
        if (!_settingsService.Settings.HookPermissionPopupsEnabled)
        {
            LoggingService.Instance.Log("PermissionRequestHandler: Popups disabled, falling back to terminal");
            return new HookResponse
            {
                RequestId = evt.RequestId,
                Success = true,
                JsonOutput = null
            };
        }

        try
        {
            var info = ParsePayload(evt.Payload);
            info.SessionId = string.IsNullOrEmpty(info.SessionId) ? evt.RequestId : info.SessionId;

            var tcs = new TaskCompletionSource<HookResponse>();
            var args = new HookInteractiveEventArgs<PermissionRequestInfo>(info, tcs);

            // Capture delegate to avoid TOCTOU race (subscriber could unsubscribe between invoke and null check)
            var handler = PermissionRequested;
            handler?.Invoke(this, args);

            if (handler == null)
            {
                LoggingService.Instance.Log("PermissionRequestHandler: No UI handler subscribed, falling back to terminal");
                return new HookResponse
                {
                    RequestId = evt.RequestId,
                    Success = true,
                    JsonOutput = null
                };
            }

            // Await the user's decision from the UI
            var response = await tcs.Task;
            response.RequestId = evt.RequestId;
            return response;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("PermissionRequestHandler: Failed to handle permission request", ex);
            return new HookResponse
            {
                RequestId = evt.RequestId,
                Success = true,
                JsonOutput = null
            };
        }
    }

    private static PermissionRequestInfo ParsePayload(string payload)
    {
        var info = new PermissionRequestInfo();

        if (string.IsNullOrWhiteSpace(payload))
            return info;

        var node = JsonNode.Parse(payload);
        if (node == null)
            return info;

        info.ToolName = node[Fields.ToolName]?.GetValue<string>() ?? string.Empty;
        info.SessionId = node[Fields.SessionId]?.GetValue<string>() ?? string.Empty;
        info.Cwd = node[Fields.Cwd]?.GetValue<string>() ?? string.Empty;
        info.PermissionMode = node[Fields.PermissionMode]?.GetValue<string>() ?? string.Empty;

        // Parse tool_input as Dictionary<string, object>
        var toolInputNode = node[Fields.ToolInput];
        if (toolInputNode != null)
        {
            info.ToolInput = ParseJsonObjectToDictionary(toolInputNode);
        }

        // Parse permission_suggestions
        var suggestionsNode = node[Fields.PermissionSuggestions];
        if (suggestionsNode is JsonArray suggestionsArray)
        {
            foreach (var suggestionNode in suggestionsArray)
            {
                if (suggestionNode == null) continue;

                var suggestion = new PermissionSuggestion
                {
                    Type = suggestionNode["type"]?.GetValue<string>() ?? string.Empty,
                    Behavior = suggestionNode["behavior"]?.GetValue<string>() ?? string.Empty,
                    Destination = suggestionNode["destination"]?.GetValue<string>() ?? string.Empty,
                    Tool = suggestionNode["tool"]?.GetValue<string>() ?? string.Empty,
                    Prefix = suggestionNode["prefix"]?.GetValue<string>() ?? string.Empty,
                    RawJson = suggestionNode.ToJsonString()
                };

                // Parse rules
                var rulesNode = suggestionNode["rules"];
                if (rulesNode is JsonArray rulesArray)
                {
                    foreach (var ruleNode in rulesArray)
                    {
                        if (ruleNode == null) continue;
                        suggestion.Rules.Add(new PermissionRule
                        {
                            ToolName = ruleNode["toolName"]?.GetValue<string>() ?? string.Empty,
                            RuleContent = ruleNode["ruleContent"]?.GetValue<string>() ?? string.Empty
                        });
                    }
                }

                // Parse directories
                var directoriesNode = suggestionNode["directories"];
                if (directoriesNode is JsonArray dirArray)
                {
                    foreach (var dirNode in dirArray)
                    {
                        if (dirNode == null) continue;
                        suggestion.Directories.Add(dirNode.GetValue<string>());
                    }
                }

                info.PermissionSuggestions.Add(suggestion);
            }
        }

        return info;
    }

    /// <summary>
    /// Builds the Claude Code response JSON from a PermissionDecisionResult.
    /// </summary>
    public static string? BuildResponseJson(PermissionDecisionResult result)
    {
        if (result.Decision == PermissionDecision.HandleInTerminal)
            return null;

        var decision = new JsonObject();

        switch (result.Decision)
        {
            case PermissionDecision.AlwaysAllow when result.AppliedSuggestion != null:
                decision[Response.Behavior] = Response.Allow;
                if (result.AppliedSuggestion.RawJson != null)
                {
                    // Echo back the original suggestion verbatim — preserves all fields
                    // Claude Code expects its own suggestion format back in updatedPermissions
                    var rawNode = JsonNode.Parse(result.AppliedSuggestion.RawJson);
                    decision[Response.UpdatedPermissions] = new JsonArray { rawNode };
                }
                else
                {
                    // Fallback: simple tool permission
                    var permEntry = new JsonObject
                    {
                        ["type"] = "toolAlwaysAllow",
                        ["tool"] = result.AppliedSuggestion.Tool
                    };
                    decision[Response.UpdatedPermissions] = new JsonArray { permEntry };
                }
                break;

            case PermissionDecision.Allow:
            case PermissionDecision.AlwaysAllow: // no suggestion — degrade to one-time allow
                decision[Response.Behavior] = Response.Allow;
                break;

            case PermissionDecision.Deny:
                decision[Response.Behavior] = Response.Deny;
                break;
        }

        // Include updated input if set (use runtime types to avoid object serialization issues)
        if (result.UpdatedInput != null && result.UpdatedInput.Count > 0)
        {
            var updatedInputNode = new JsonObject();
            foreach (var kvp in result.UpdatedInput)
            {
                if (kvp.Value == null)
                {
                    updatedInputNode[kvp.Key] = null;
                }
                else
                {
                    updatedInputNode[kvp.Key] = JsonSerializer.SerializeToNode(
                        kvp.Value, kvp.Value.GetType());
                }
            }
            decision[Response.UpdatedInput] = updatedInputNode;
        }

        var hookOutput = new JsonObject
        {
            [Response.HookEventName] = Events.PermissionRequest,
            [Response.Decision] = decision
        };

        var root = new JsonObject
        {
            [Response.HookSpecificOutput] = hookOutput
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static Dictionary<string, object> ParseJsonObjectToDictionary(JsonNode node, int depth = 0)
    {
        var dict = new Dictionary<string, object>();

        if (depth > 8 || node is not JsonObject obj)
            return dict;

        foreach (var kvp in obj)
        {
            dict[kvp.Key] = ParseJsonValue(kvp.Value, depth + 1);
        }

        return dict;
    }

    private static object ParseJsonValue(JsonNode? node, int depth = 0)
    {
        if (node == null) return string.Empty;
        if (depth > 8) return node.ToJsonString();

        return node switch
        {
            JsonObject obj => ParseJsonObjectToDictionary(obj, depth),
            JsonArray arr => arr.Select(n => ParseJsonValue(n, depth + 1)).ToList(),
            JsonValue val => val.TryGetValue<bool>(out var b) ? b
                : val.TryGetValue<long>(out var l) ? l
                : val.TryGetValue<double>(out var d) ? d
                : val.GetValue<string>() ?? string.Empty,
            _ => node.ToJsonString()
        };
    }
}
