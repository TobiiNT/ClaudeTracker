using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

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

    public bool CanHandle(string eventName) => eventName == "PermissionRequest";

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

            PermissionRequested?.Invoke(this, args);

            if (PermissionRequested == null)
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

        info.ToolName = node["tool_name"]?.GetValue<string>() ?? string.Empty;
        info.SessionId = node["session_id"]?.GetValue<string>() ?? string.Empty;
        info.Cwd = node["cwd"]?.GetValue<string>() ?? string.Empty;
        info.PermissionMode = node["permission_mode"]?.GetValue<string>() ?? string.Empty;

        // Parse tool_input as Dictionary<string, object>
        var toolInputNode = node["tool_input"];
        if (toolInputNode != null)
        {
            info.ToolInput = ParseJsonObjectToDictionary(toolInputNode);
        }

        // Parse permission_suggestions
        var suggestionsNode = node["permission_suggestions"];
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
                    Prefix = suggestionNode["prefix"]?.GetValue<string>() ?? string.Empty
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
                decision["behavior"] = "allow";
                var suggestion = new JsonObject
                {
                    ["type"] = result.AppliedSuggestion.Type,
                    ["behavior"] = result.AppliedSuggestion.Behavior,
                    ["destination"] = result.AppliedSuggestion.Destination,
                    ["tool"] = result.AppliedSuggestion.Tool,
                    ["prefix"] = result.AppliedSuggestion.Prefix
                };

                if (result.AppliedSuggestion.Rules.Count > 0)
                {
                    var rulesArr = new JsonArray();
                    foreach (var rule in result.AppliedSuggestion.Rules)
                    {
                        rulesArr.Add(new JsonObject
                        {
                            ["toolName"] = rule.ToolName,
                            ["ruleContent"] = rule.RuleContent
                        });
                    }
                    suggestion["rules"] = rulesArr;
                }

                if (result.AppliedSuggestion.Directories.Count > 0)
                {
                    var dirsArr = new JsonArray();
                    foreach (var dir in result.AppliedSuggestion.Directories)
                    {
                        dirsArr.Add(dir);
                    }
                    suggestion["directories"] = dirsArr;
                }

                decision["updatedPermissions"] = new JsonArray { suggestion };
                break;

            case PermissionDecision.Allow:
                decision["behavior"] = "allow";
                break;

            case PermissionDecision.Deny:
                decision["behavior"] = "deny";
                break;

            case PermissionDecision.AlwaysAllow:
                decision["behavior"] = "allow";
                break;
        }

        // Include updated input if set
        if (result.UpdatedInput != null && result.UpdatedInput.Count > 0)
        {
            var updatedInputNode = new JsonObject();
            foreach (var kvp in result.UpdatedInput)
            {
                updatedInputNode[kvp.Key] = JsonSerializer.SerializeToNode(kvp.Value);
            }
            decision["updatedInput"] = updatedInputNode;
        }

        var hookOutput = new JsonObject
        {
            ["hookEventName"] = "PermissionRequest",
            ["decision"] = decision
        };

        var root = new JsonObject
        {
            ["hookSpecificOutput"] = hookOutput
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static Dictionary<string, object> ParseJsonObjectToDictionary(JsonNode node)
    {
        var dict = new Dictionary<string, object>();

        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                dict[kvp.Key] = ParseJsonValue(kvp.Value);
            }
        }

        return dict;
    }

    private static object ParseJsonValue(JsonNode? node)
    {
        if (node == null) return string.Empty;

        return node switch
        {
            JsonObject obj => ParseJsonObjectToDictionary(obj),
            JsonArray arr => arr.Select(ParseJsonValue).ToList(),
            JsonValue val => val.TryGetValue<bool>(out var b) ? b
                : val.TryGetValue<long>(out var l) ? l
                : val.TryGetValue<double>(out var d) ? d
                : val.GetValue<string>() ?? string.Empty,
            _ => node.ToJsonString()
        };
    }
}
