using Moq;
using Xunit;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Services.Observers;

namespace ClaudeTracker.Tests;

public class SessionTrackerTests
{
    private readonly Mock<ISessionTrackingService> _sessionTracking = new();
    private readonly SessionTracker _tracker;

    public SessionTrackerTests()
    {
        _tracker = new SessionTracker(_sessionTracking.Object);
    }

    [Fact]
    public void Observe_SessionStart_RegistersSession()
    {
        var evt = new HookEvent
        {
            EventName = "SessionStart",
            Payload = """{"session_id":"s1","cwd":"/project","permission_mode":"default","model":"opus"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.RegisterSession("s1", "/project", "default", "opus"), Times.Once);
    }

    [Fact]
    public void Observe_SessionEnd_EndsSession()
    {
        var evt = new HookEvent
        {
            EventName = "SessionEnd",
            Payload = """{"session_id":"s1","reason":"prompt_input_exit"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.EndSession("s1"), Times.Once);
    }

    [Fact]
    public void Observe_SubagentStart_RegistersSubagent()
    {
        var evt = new HookEvent
        {
            EventName = "SubagentStart",
            Payload = """{"session_id":"s1","agent_id":"a1","agent_type":"Explore"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.RegisterSubagent("s1", "a1", "Explore"), Times.Once);
    }

    [Fact]
    public void Observe_SubagentStop_EndsSubagent()
    {
        var evt = new HookEvent
        {
            EventName = "SubagentStop",
            Payload = """{"session_id":"s1","agent_id":"a1"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.EndSubagent("s1", "a1"), Times.Once);
    }

    [Fact]
    public void Observe_EmptySessionId_DoesNothing()
    {
        var evt = new HookEvent
        {
            EventName = "SessionStart",
            Payload = """{"cwd":"/project"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.RegisterSession(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Observe_UnrelatedEvent_DoesNothing()
    {
        var evt = new HookEvent
        {
            EventName = "PostToolUse",
            Payload = """{"session_id":"s1","tool_name":"Bash"}"""
        };

        _tracker.Observe(evt);

        _sessionTracking.Verify(s => s.RegisterSession(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _sessionTracking.Verify(s => s.EndSession(It.IsAny<string>()), Times.Never);
    }
}
