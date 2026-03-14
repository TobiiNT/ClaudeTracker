using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IHookIpcService : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    event Func<HookEvent, Task<HookResponse>>? EventReceived;
}
