using System.Collections.ObjectModel;
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface ISessionTrackingService
{
    ObservableCollection<SessionState> ActiveSessions { get; }
    int ActiveSessionCount { get; }
    event EventHandler? SessionsChanged;
    void RegisterSession(string sessionId, string projectDirectory, string permissionMode, string? model);
    void EndSession(string sessionId);
    void RecordActivity(string sessionId, ActivityEntry entry);
    void RegisterSubagent(string sessionId, string agentId, string? agentType);
    void EndSubagent(string sessionId, string agentId);
    void PruneStale();
}
