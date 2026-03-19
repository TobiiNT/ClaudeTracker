using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using Microsoft.Win32.SafeHandles;

namespace ClaudeTracker.Services;

/// <summary>
/// Named pipe server that receives hook events from HookBridge instances.
/// Uses a 4-byte little-endian length prefix + UTF-8 JSON wire protocol.
/// </summary>
public class HookIpcService : IHookIpcService
{
    private CancellationTokenSource? _cts;
    private readonly List<Task> _listeners = new();
    private readonly object _lock = new();
    private bool _disposed;
    private System.Windows.Threading.DispatcherTimer? _watchdog;

    public bool IsRunning { get; private set; }

    public event Func<HookEvent, Task<HookResponse>>? EventReceived;

    /// <summary>Fired when a HookBridge client disconnects (user answered in terminal).</summary>
    public event EventHandler<string>? PipeDisconnected;

    /// <summary>Fired when any hook event is received, before dispatching.</summary>
    public event EventHandler<HookEvent>? EventArrived;

    private void OnPipeDisconnected(string requestId)
    {
        PipeDisconnected?.Invoke(this, requestId);
    }

    // Win32 interop for detecting pipe client disconnect
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(
        SafePipeHandle hNamedPipe,
        IntPtr lpBuffer,
        uint nBufferSize,
        IntPtr lpBytesRead,
        IntPtr lpTotalBytesAvail,
        IntPtr lpBytesLeftThisMessage);

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning || _disposed) return;

            _cts = new CancellationTokenSource();
            IsRunning = true;

            LoggingService.Instance.Log($"[HookIpc] Starting named pipe server on '{Constants.Hooks.PipeName}'");

            // Spin up concurrent listeners
            for (var i = 0; i < Constants.Hooks.MaxConcurrentConnections; i++)
            {
                var listenerTask = Task.Run(() => ListenLoop(_cts.Token));
                _listeners.Add(listenerTask);
            }

            // Watchdog: check listener health every 30s, restart if all dead
            _watchdog = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _watchdog.Tick += (_, _) => CheckAndRestartListeners();
            _watchdog.Start();
        }
    }

    public void Stop()
    {
        List<Task> tasksToAwait;
        lock (_lock)
        {
            if (!IsRunning) return;

            LoggingService.Instance.Log("[HookIpc] Stopping named pipe server");

            _watchdog?.Stop();
            _watchdog = null;
            _cts?.Cancel();
            IsRunning = false;

            tasksToAwait = new List<Task>(_listeners);
            _listeners.Clear();
        }

        // Await outside lock to avoid holding lock during shutdown
        try
        {
            Task.WhenAll(tasksToAwait).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Expected — tasks cancelled
        }

        lock (_lock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CheckAndRestartListeners()
    {
        lock (_lock)
        {
            if (!IsRunning || _cts == null || _cts.IsCancellationRequested) return;

            // Remove completed tasks and count survivors
            _listeners.RemoveAll(t => t.IsCompleted);

            if (_listeners.Count == 0)
            {
                LoggingService.Instance.LogWarning("[HookIpc] All listeners dead — restarting");
                for (var i = 0; i < Constants.Hooks.MaxConcurrentConnections; i++)
                {
                    _listeners.Add(Task.Run(() => ListenLoop(_cts.Token)));
                }
            }
            else if (_listeners.Count < Constants.Hooks.MaxConcurrentConnections)
            {
                var toAdd = Constants.Hooks.MaxConcurrentConnections - _listeners.Count;
                LoggingService.Instance.Log($"[HookIpc] {toAdd} listener(s) died — respawning");
                for (var i = 0; i < toAdd; i++)
                {
                    _listeners.Add(Task.Run(() => ListenLoop(_cts.Token)));
                }
            }
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipeWithSecurity();

                await pipe.WaitForConnectionAsync(ct);

                await HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError("[HookIpc] Listener error", ex);
                // Delay before retry to avoid CPU burn on persistent errors
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (pipe != null)
                {
                    try
                    {
                        if (pipe.IsConnected)
                            pipe.Disconnect();
                    }
                    catch
                    {
                        // Ignore disconnect errors
                    }

                    await pipe.DisposeAsync();
                }
            }
        }
    }

    /// <summary>
    /// Creates a named pipe server with an explicit ACL granting the current user full control.
    /// Without this, pipe clients from the same user but different security context (e.g. Claude Code
    /// spawning HookBridge) get UnauthorizedAccessException.
    /// </summary>
    private static NamedPipeServerStream CreatePipeWithSecurity()
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            Constants.Hooks.PipeName,
            PipeDirection.InOut,
            Constants.Hooks.MaxConcurrentConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: ps);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // 1. Read the hook event
        var evt = await ReadEventAsync(pipe, ct);
        if (evt == null)
        {
            LoggingService.Instance.LogWarning("[HookIpc] Failed to read event from pipe");
            return;
        }

        LoggingService.Instance.Log($"[HookIpc] Received event: {evt.EventName} (requestId={evt.RequestId})");

        // Notify that an event arrived (used to close stale popups)
        try { EventArrived?.Invoke(this, evt); }
        catch (Exception ex) { LoggingService.Instance.LogError("[HookIpc] EventArrived handler error", ex); }

        // 2. Build default response
        var defaultResponse = new HookResponse
        {
            RequestId = evt.RequestId,
            Success = true
        };

        // 3. Invoke handler
        var handler = EventReceived;
        if (handler == null)
        {
            await WriteResponseAsync(pipe, defaultResponse, ct);
            return;
        }

        // 4. For interactive events, monitor pipe disconnect (HookBridge/user answered in terminal)
        if (Constants.Hooks.InteractiveEvents.Contains(evt.EventName))
        {
            using var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var handlerTask = handler.Invoke(evt);
            var disconnectTcs = new TaskCompletionSource<HookResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Register cancellation to unblock the disconnect monitor
            using var ctReg = ct.Register(() => disconnectTcs.TrySetCanceled());

            var monitorTask = MonitorPipeDisconnectAsync(pipe, disconnectTcs, ct);

            var completedTask = await Task.WhenAny(handlerTask, disconnectTcs.Task);

            HookResponse response;
            if (completedTask == handlerTask)
            {
                try
                {
                    response = await handlerTask;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.LogError($"[HookIpc] Handler error for {evt.EventName}", ex);
                    response = defaultResponse;
                }

                // Cancel the disconnect monitor
                disconnectTcs.TrySetCanceled();
            }
            else
            {
                // Client disconnected — user answered in terminal.
                // Fire the PipeDisconnected event so UI popups can auto-close.
                LoggingService.Instance.Log($"[HookIpc] Client disconnected for {evt.EventName}, notifying UI");
                response = defaultResponse;
                OnPipeDisconnected(evt.RequestId);
            }

            // Only write if pipe is still connected
            if (pipe.IsConnected)
            {
                await WriteResponseAsync(pipe, response, ct);
            }
        }
        else
        {
            // Non-interactive: just invoke handler and respond
            HookResponse response;
            try
            {
                response = await handler.Invoke(evt);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError($"[HookIpc] Handler error for {evt.EventName}", ex);
                response = defaultResponse;
            }

            if (pipe.IsConnected)
            {
                await WriteResponseAsync(pipe, response, ct);
            }
        }
    }

    private async Task<HookEvent?> ReadEventAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Read 4-byte length prefix (little-endian)
        var lengthBytes = await ReadExactAsync(pipe, 4, ct);
        if (lengthBytes == null)
            return null;

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
        if (messageLength <= 0 || messageLength > Constants.Hooks.MaxMessageSize)
        {
            LoggingService.Instance.LogError($"[HookIpc] Invalid message length: {messageLength}");
            return null;
        }

        // Read payload
        var payloadBytes = await ReadExactAsync(pipe, messageLength, ct);
        if (payloadBytes == null)
            return null;

        var json = Encoding.UTF8.GetString(payloadBytes);

        try
        {
            return JsonSerializer.Deserialize<HookEvent>(json);
        }
        catch (JsonException ex)
        {
            LoggingService.Instance.LogError("[HookIpc] Failed to deserialize event", ex);
            return null;
        }
    }

    private async Task WriteResponseAsync(NamedPipeServerStream pipe, HookResponse response, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(response);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var lengthPrefix = BitConverter.GetBytes(payloadBytes.Length);

            await pipe.WriteAsync(lengthPrefix.AsMemory(0, 4), ct);
            await pipe.WriteAsync(payloadBytes.AsMemory(0, payloadBytes.Length), ct);
            await pipe.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("[HookIpc] Failed to write response", ex);
        }
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from the stream.
    /// Returns null if the stream ends before all bytes are read.
    /// </summary>
    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (bytesRead == 0)
                return null; // Stream ended prematurely

            offset += bytesRead;
        }

        return buffer;
    }

    /// <summary>
    /// Polls the pipe using Win32 PeekNamedPipe to detect client disconnect.
    /// When the client disconnects, sets the TCS result so the handler can be cancelled.
    /// </summary>
    private async Task MonitorPipeDisconnectAsync(
        NamedPipeServerStream pipe,
        TaskCompletionSource<HookResponse> disconnectTcs,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !disconnectTcs.Task.IsCompleted)
            {
                await Task.Delay(100, ct);

                if (!pipe.IsConnected)
                {
                    disconnectTcs.TrySetResult(new HookResponse { Success = true });
                    return;
                }

                // Use PeekNamedPipe to detect broken pipe
                var result = PeekNamedPipe(
                    pipe.SafePipeHandle,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (!result)
                {
                    // PeekNamedPipe failed — pipe is broken, client disconnected
                    disconnectTcs.TrySetResult(new HookResponse { Success = true });
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal — handler completed or shutdown
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("[HookIpc] Pipe disconnect monitor error", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
