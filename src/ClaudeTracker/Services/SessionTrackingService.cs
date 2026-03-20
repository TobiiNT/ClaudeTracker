using System.Collections.ObjectModel;
using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

/// <summary>
/// Manages active Claude Code sessions with thread-safe locking.
/// All methods use dispatch-then-lock pattern to prevent deadlocks:
/// Dispatcher.Invoke(() => { lock (_lock) { ... } })
/// </summary>
public class SessionTrackingService : ISessionTrackingService
{
    private readonly object _lock = new();

    public ObservableCollection<SessionState> ActiveSessions { get; } = new();
    public int ActiveSessionCount => ActiveSessions.Count;
    public event EventHandler? SessionsChanged;

    public void RegisterSession(string sessionId, string projectDirectory, string permissionMode, string? model, long? consoleWindowHandle = null)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var existing = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (existing != null)
                {
                    existing.CurrentActivity = "Resumed";
                    existing.LastActivityTime = DateTime.UtcNow;
                    return;
                }

                var session = new SessionState
                {
                    SessionId = sessionId,
                    Cwd = projectDirectory,
                    Model = model ?? "",
                    ConsoleWindowHandle = consoleWindowHandle
                };
                ActiveSessions.Add(session);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void EndSession(string sessionId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    ActiveSessions.Remove(session);
                }
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void RecordActivity(string sessionId, ActivityEntry entry)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null) return;

                session.CurrentActivity = entry.Summary;
                session.LastActivityTime = DateTime.UtcNow;
                if (entry.ToolName != null) session.ToolCallCount++;

                session.Activities.Insert(0, entry);
                while (session.Activities.Count > Constants.Hooks.DefaultMaxActivityEntries)
                    session.Activities.RemoveAt(session.Activities.Count - 1);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void RegisterSubagent(string sessionId, string agentId, string? agentType)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null) return;
                if (!session.ActiveSubagents.Contains(agentId))
                {
                    session.ActiveSubagents.Add(agentId);
                    session.SubagentCount++;
                }
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void EndSubagent(string sessionId, string agentId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                session?.ActiveSubagents.Remove(agentId);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void PruneStale()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Constants.Hooks.StaleSessionMinutes);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var stale = ActiveSessions
                    .Where(s => s.LastActivityTime < cutoff)
                    .ToList();
                foreach (var s in stale) ActiveSessions.Remove(s);
                if (stale.Count > 0) SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }
}
