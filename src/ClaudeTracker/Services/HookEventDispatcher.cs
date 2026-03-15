using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

/// <summary>
/// Routes incoming HookEvents to the appropriate handler and broadcasts to all observers.
/// Observers are fire-and-forget (never block the response).
/// Handlers are invoked only for interactive events and return a HookResponse.
/// </summary>
public class HookEventDispatcher : IHookEventDispatcher
{
    private readonly IHookIpcService _ipcService;
    private readonly IEnumerable<IHookEventHandler> _handlers;
    private readonly IEnumerable<IHookEventObserver> _observers;

    public HookEventDispatcher(
        IHookIpcService ipcService,
        IEnumerable<IHookEventHandler> handlers,
        IEnumerable<IHookEventObserver> observers)
    {
        _ipcService = ipcService;
        _handlers = handlers;
        _observers = observers;
    }

    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _ipcService.EventReceived += OnEventReceived;
    }

    private async Task<HookResponse> OnEventReceived(HookEvent evt)
    {
        // Broadcast to all observers (fire-and-forget, never blocks)
        foreach (var observer in _observers)
        {
            try { observer.Observe(evt); }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError($"Observer error for {evt.EventName}", ex);
            }
        }

        // Route to interactive handler if one matches
        if (Constants.Hooks.InteractiveEvents.Contains(evt.EventName))
        {
            foreach (var handler in _handlers)
            {
                if (handler.CanHandle(evt.EventName))
                {
                    try { return await handler.HandleAsync(evt); }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.LogError($"Handler error for {evt.EventName}", ex);
                    }
                }
            }
        }

        return new HookResponse { RequestId = evt.RequestId, Success = true };
    }
}
