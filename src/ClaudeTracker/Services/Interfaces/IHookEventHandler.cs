using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IHookEventHandler
{
    bool CanHandle(string eventName);
    Task<HookResponse> HandleAsync(HookEvent evt);
}
