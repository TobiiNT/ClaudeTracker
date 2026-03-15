using Moq;
using Xunit;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Services.Observers;

namespace ClaudeTracker.Tests;

public class ActivityRecorderTests
{
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<ISessionTrackingService> _sessionTracking = new();
    private readonly ActivityRecorder _recorder;
    private ActivityEntry? _recorded;

    public ActivityRecorderTests()
    {
        _activityService.Setup(a => a.Record(It.IsAny<ActivityEntry>()))
            .Callback<ActivityEntry>(e => _recorded = e);
        _recorder = new ActivityRecorder(_activityService.Object, _sessionTracking.Object);
    }

    [Fact]
    public void Observe_BashTool_FormatsCommandSummary()
    {
        var evt = CreateEvent("PreToolUse", """{"tool_name":"Bash","tool_input":{"command":"npm test"},"session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Contains("Bash: npm test", _recorded!.Summary);
        Assert.Equal(ActivityIcon.Tool, _recorded.Icon);
        Assert.Equal("Bash", _recorded.ToolName);
    }

    [Fact]
    public void Observe_EditTool_FormatsFilePath()
    {
        var evt = CreateEvent("PostToolUse", """{"tool_name":"Edit","tool_input":{"file_path":"src/App.cs"},"session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Contains("Edit src/App.cs", _recorded!.Summary);
    }

    [Fact]
    public void Observe_SessionStart_FormatsSource()
    {
        var evt = CreateEvent("SessionStart", """{"source":"startup","session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Equal("Session started (startup)", _recorded!.Summary);
        Assert.Equal(ActivityIcon.Session, _recorded.Icon);
    }

    [Fact]
    public void Observe_Stop_ShowsTaskCompleted()
    {
        var evt = CreateEvent("Stop", """{"session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Equal("Task completed", _recorded!.Summary);
    }

    [Fact]
    public void Observe_SubagentStart_ShowsAgentType()
    {
        var evt = CreateEvent("SubagentStart", """{"agent_type":"Explore","session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Contains("Subagent: Explore", _recorded!.Summary);
        Assert.Equal(ActivityIcon.Subagent, _recorded.Icon);
    }

    [Fact]
    public void Observe_Notification_ShowsMessage()
    {
        var evt = CreateEvent("Notification", """{"message":"Rate limited","session_id":"s1"}""");
        _recorder.Observe(evt);
        Assert.Equal("Rate limited", _recorded!.Summary);
        Assert.Equal(ActivityIcon.Notification, _recorded.Icon);
    }

    [Fact]
    public void Observe_InvalidJson_DoesNotCrash()
    {
        var evt = CreateEvent("PostToolUse", "not valid json");
        _recorder.Observe(evt);
        _activityService.Verify(a => a.Record(It.IsAny<ActivityEntry>()), Times.Once);
    }

    [Fact]
    public void Observe_RecordsToSessionTracking()
    {
        var evt = CreateEvent("PreToolUse", """{"tool_name":"Bash","tool_input":{"command":"ls"},"session_id":"sess123"}""");
        _recorder.Observe(evt);
        _sessionTracking.Verify(s => s.RecordActivity("sess123", It.IsAny<ActivityEntry>()), Times.Once);
    }

    [Fact]
    public void Observe_EmptySessionId_SkipsSessionTracking()
    {
        var evt = CreateEvent("PreToolUse", """{"tool_name":"Bash","tool_input":{"command":"ls"}}""");
        _recorder.Observe(evt);
        _sessionTracking.Verify(s => s.RecordActivity(It.IsAny<string>(), It.IsAny<ActivityEntry>()), Times.Never);
    }

    private static HookEvent CreateEvent(string eventName, string payload) =>
        new() { RequestId = Guid.NewGuid().ToString(), EventName = eventName, Payload = payload };
}
