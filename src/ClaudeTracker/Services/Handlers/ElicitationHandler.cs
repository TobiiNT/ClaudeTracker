using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles Elicitation events from Claude Code (MCP server prompts).
/// Parses the elicitation payload, raises an event for the UI popup,
/// and awaits the user's decision before returning the response.
/// </summary>
public class ElicitationHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;

    /// <summary>Raised when an elicitation request requires user interaction.</summary>
    public event EventHandler<HookInteractiveEventArgs<ElicitationInfo>>? ElicitationRequested;

    public ElicitationHandler(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool CanHandle(string eventName) => eventName == Events.Elicitation;

    public async Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"ElicitationHandler: Processing request {evt.RequestId}");

        // If elicitation popups are disabled, return success with no jsonOutput
        // (falls back to terminal handling)
        if (!_settingsService.Settings.HookElicitationPopupsEnabled)
        {
            LoggingService.Instance.Log("ElicitationHandler: Popups disabled, falling back to terminal");
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
            var args = new HookInteractiveEventArgs<ElicitationInfo>(info, tcs);

            ElicitationRequested?.Invoke(this, args);

            if (ElicitationRequested == null)
            {
                LoggingService.Instance.Log("ElicitationHandler: No UI handler subscribed, falling back to terminal");
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
            LoggingService.Instance.LogError("ElicitationHandler: Failed to handle elicitation request", ex);
            return new HookResponse
            {
                RequestId = evt.RequestId,
                Success = true,
                JsonOutput = null
            };
        }
    }

    private static ElicitationInfo ParsePayload(string payload)
    {
        var info = new ElicitationInfo();

        if (string.IsNullOrWhiteSpace(payload))
            return info;

        var node = JsonNode.Parse(payload);
        if (node == null)
            return info;

        info.McpServerName = node["mcp_server_name"]?.GetValue<string>() ?? string.Empty;
        info.Message = node[Fields.Message]?.GetValue<string>() ?? string.Empty;
        info.Mode = node["mode"]?.GetValue<string>() ?? string.Empty;
        info.Url = node[Fields.Url]?.GetValue<string>() ?? string.Empty;
        info.ElicitationId = node["elicitation_id"]?.GetValue<string>() ?? string.Empty;
        info.SessionId = node[Fields.SessionId]?.GetValue<string>() ?? string.Empty;
        info.Cwd = node[Fields.Cwd]?.GetValue<string>() ?? string.Empty;

        // Parse requested_schema as Dictionary<string, object>
        var schemaNode = node["requested_schema"];
        if (schemaNode is JsonObject schemaObj)
        {
            foreach (var kvp in schemaObj)
            {
                info.RequestedSchema[kvp.Key] = kvp.Value?.ToJsonString() ?? string.Empty;
            }
        }

        return info;
    }
}
