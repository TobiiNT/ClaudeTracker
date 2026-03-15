using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IHookEventObserver
{
    void Observe(HookEvent evt);
}
