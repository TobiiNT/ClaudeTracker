using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

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

        var sessionId = json["session_id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(sessionId)) return;

        switch (evt.EventName)
        {
            case "SessionStart":
                _sessionTracking.RegisterSession(
                    sessionId,
                    json["cwd"]?.GetValue<string>() ?? "",
                    json["permission_mode"]?.GetValue<string>() ?? "",
                    json["model"]?.GetValue<string>());
                break;

            case "SessionEnd":
                _sessionTracking.EndSession(sessionId);
                break;

            case "SubagentStart":
                var agentId = json["agent_id"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(agentId))
                    _sessionTracking.RegisterSubagent(sessionId, agentId, json["agent_type"]?.GetValue<string>());
                break;

            case "SubagentStop":
                var endAgentId = json["agent_id"]?.GetValue<string>() ?? "";
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
