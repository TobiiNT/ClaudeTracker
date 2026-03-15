using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IHookIpcService : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    event Func<HookEvent, Task<HookResponse>>? EventReceived;

    /// <summary>Fired when a HookBridge client disconnects (user answered in terminal).</summary>
    event EventHandler<string>? PipeDisconnected;

    /// <summary>Fired when any hook event is received, before dispatching. Used to detect stale popups.</summary>
    event EventHandler<HookEvent>? EventArrived;
}
