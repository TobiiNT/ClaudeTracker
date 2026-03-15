using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles PreToolUse events from Claude Code.
/// Simple passthrough — returns success without jsonOutput to let Claude proceed.
/// </summary>
public class PreToolUseHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == Events.PreToolUse;

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"PreToolUseHandler: Passthrough for request {evt.RequestId}");

        return Task.FromResult(new HookResponse
        {
            RequestId = evt.RequestId,
            Success = true,
            JsonOutput = null
        });
    }
}
