using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Observers;

/// <summary>
/// Observes session lifecycle events (SessionStart, SessionEnd, SubagentStart, SubagentStop)
/// and updates the session tracking service accordingly.
/// </summary>
public class SessionTracker : IHookEventObserver
{
    private readonly ISessionTrackingService _sessionTracking;

    public SessionTracker(ISessionTrackingService sessionTracking)
    {
        _sessionTracking = sessionTracking;
    }

    public void Observe(HookEvent evt)
    {
        var json = TryParse(evt.Payload);
        if (json == null) return;

        var sessionId = json[Fields.SessionId]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(sessionId)) return;

        switch (evt.EventName)
        {
            case Events.SessionStart:
                _sessionTracking.RegisterSession(
                    sessionId,
                    json[Fields.Cwd]?.GetValue<string>() ?? "",
                    json[Fields.PermissionMode]?.GetValue<string>() ?? "",
                    json[Fields.Model]?.GetValue<string>());
                break;

            case Events.SessionEnd:
                _sessionTracking.EndSession(sessionId);
                break;

            case Events.SubagentStart:
                var agentId = json[Fields.AgentId]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(agentId))
                    _sessionTracking.RegisterSubagent(sessionId, agentId, json[Fields.AgentType]?.GetValue<string>());
                break;

            case Events.SubagentStop:
                var endAgentId = json[Fields.AgentId]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(endAgentId))
                    _sessionTracking.EndSubagent(sessionId, endAgentId);
                break;
        }
    }

    private static JsonNode? TryParse(string payload)
    {
        try { return JsonNode.Parse(payload); } catch { return null; }
    }
}
