using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles ConfigChange events from Claude Code.
/// Simple passthrough — returns success to let Claude proceed.
/// </summary>
public class ConfigChangeHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == Events.ConfigChange;

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"ConfigChangeHandler: Passthrough for request {evt.RequestId}");

        return Task.FromResult(new HookResponse
        {
            RequestId = evt.RequestId,
            Success = true,
            JsonOutput = null
        });
    }
}
