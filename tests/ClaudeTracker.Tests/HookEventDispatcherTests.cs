using Moq;
using Xunit;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Tests;

public class HookEventDispatcherTests
{
    // Test: Interactive event routes to matching handler
    [Fact]
    public async Task OnEventReceived_InteractiveEvent_RoutesToHandler()
    {
        // Arrange
        var ipcService = new Mock<IHookIpcService>();
        Func<HookEvent, Task<HookResponse>>? capturedCallback = null;
        ipcService.SetupAdd(s => s.EventReceived += It.IsAny<Func<HookEvent, Task<HookResponse>>>())
            .Callback<Func<HookEvent, Task<HookResponse>>>(cb => capturedCallback = cb);

        var handler = new Mock<IHookEventHandler>();
        handler.Setup(h => h.CanHandle("PermissionRequest")).Returns(true);
        handler.Setup(h => h.HandleAsync(It.IsAny<HookEvent>()))
            .ReturnsAsync(new HookResponse { RequestId = "test", Success = true, JsonOutput = "result" });

        var observer = new Mock<IHookEventObserver>();

        var dispatcher = new HookEventDispatcher(
            ipcService.Object,
            new[] { handler.Object },
            new[] { observer.Object });
        dispatcher.Initialize();

        // Act
        var evt = new HookEvent { RequestId = "req1", EventName = "PermissionRequest", Payload = "{}" };
        var response = await capturedCallback!(evt);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("result", response.JsonOutput);
        handler.Verify(h => h.HandleAsync(It.IsAny<HookEvent>()), Times.Once);
        observer.Verify(o => o.Observe(It.IsAny<HookEvent>()), Times.Once);
    }

    // Test: Non-interactive event returns default success (no handler called)
    [Fact]
    public async Task OnEventReceived_NonInteractiveEvent_ReturnsDefaultSuccess()
    {
        var ipcService = new Mock<IHookIpcService>();
        Func<HookEvent, Task<HookResponse>>? capturedCallback = null;
        ipcService.SetupAdd(s => s.EventReceived += It.IsAny<Func<HookEvent, Task<HookResponse>>>())
            .Callback<Func<HookEvent, Task<HookResponse>>>(cb => capturedCallback = cb);

        var handler = new Mock<IHookEventHandler>();
        var observer = new Mock<IHookEventObserver>();

        var dispatcher = new HookEventDispatcher(
            ipcService.Object,
            new[] { handler.Object },
            new[] { observer.Object });
        dispatcher.Initialize();

        var evt = new HookEvent { RequestId = "req2", EventName = "PostToolUse", Payload = "{}" };
        var response = await capturedCallback!(evt);

        Assert.True(response.Success);
        Assert.Null(response.JsonOutput);
        handler.Verify(h => h.HandleAsync(It.IsAny<HookEvent>()), Times.Never);
        observer.Verify(o => o.Observe(It.IsAny<HookEvent>()), Times.Once);
    }

    // Test: All events broadcast to ALL observers
    [Fact]
    public async Task OnEventReceived_AlwaysBroadcastsToAllObservers()
    {
        var ipcService = new Mock<IHookIpcService>();
        Func<HookEvent, Task<HookResponse>>? capturedCallback = null;
        ipcService.SetupAdd(s => s.EventReceived += It.IsAny<Func<HookEvent, Task<HookResponse>>>())
            .Callback<Func<HookEvent, Task<HookResponse>>>(cb => capturedCallback = cb);

        var observer1 = new Mock<IHookEventObserver>();
        var observer2 = new Mock<IHookEventObserver>();

        var dispatcher = new HookEventDispatcher(
            ipcService.Object,
            Array.Empty<IHookEventHandler>(),
            new[] { observer1.Object, observer2.Object });
        dispatcher.Initialize();

        var evt = new HookEvent { RequestId = "req3", EventName = "PostToolUse", Payload = "{}" };
        await capturedCallback!(evt);

        observer1.Verify(o => o.Observe(evt), Times.Once);
        observer2.Verify(o => o.Observe(evt), Times.Once);
    }

    // Test: Observer exception doesn't crash dispatcher
    [Fact]
    public async Task OnEventReceived_ObserverThrows_ContinuesProcessing()
    {
        var ipcService = new Mock<IHookIpcService>();
        Func<HookEvent, Task<HookResponse>>? capturedCallback = null;
        ipcService.SetupAdd(s => s.EventReceived += It.IsAny<Func<HookEvent, Task<HookResponse>>>())
            .Callback<Func<HookEvent, Task<HookResponse>>>(cb => capturedCallback = cb);

        var badObserver = new Mock<IHookEventObserver>();
        badObserver.Setup(o => o.Observe(It.IsAny<HookEvent>())).Throws<InvalidOperationException>();
        var goodObserver = new Mock<IHookEventObserver>();

        var dispatcher = new HookEventDispatcher(
            ipcService.Object,
            Array.Empty<IHookEventHandler>(),
            new[] { badObserver.Object, goodObserver.Object });
        dispatcher.Initialize();

        var evt = new HookEvent { RequestId = "req4", EventName = "SessionStart", Payload = "{}" };
        var response = await capturedCallback!(evt);

        Assert.True(response.Success);
        goodObserver.Verify(o => o.Observe(evt), Times.Once);
    }
}
