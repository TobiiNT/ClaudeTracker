using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles SubagentStop events from Claude Code.
/// Simple passthrough — returns success to let Claude proceed.
/// </summary>
public class SubagentStopHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == Events.SubagentStop;

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"SubagentStopHandler: Passthrough for request {evt.RequestId}");

        return Task.FromResult(new HookResponse
        {
            RequestId = evt.RequestId,
            Success = true,
            JsonOutput = null
        });
    }
}
