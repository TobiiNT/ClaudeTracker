using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Coordinates periodic usage data refresh.</summary>
public interface IUsageRefreshCoordinator
{
    /// <summary>Current Claude system status (cached, refreshed every 5 minutes).</summary>
    ClaudeStatus CurrentStatus { get; }

    /// <summary>Starts the periodic refresh timer.</summary>
    void Start();
    /// <summary>Stops the periodic refresh timer.</summary>
    void Stop();
    /// <summary>Triggers an immediate out-of-cycle refresh.</summary>
    void RefreshNow();
    /// <summary>Invalidates API fetch cache so next RefreshNow() re-fetches immediately.</summary>
    void InvalidateApiCache();
    /// <summary>Updates the refresh interval in seconds.</summary>
    void UpdateInterval(double seconds);
    /// <summary>Whether the refresh timer is currently running.</summary>
    bool IsRunning { get; }
    /// <summary>Whether requests are currently rate-limited.</summary>
    bool IsRateLimited { get; }
    /// <summary>When the rate limit expires (UTC). Only meaningful when IsRateLimited is true.</summary>
    DateTime RateLimitedUntil { get; }

    event EventHandler? RefreshStarted;
    event EventHandler? RefreshCompleted;
    event EventHandler<string>? RefreshFailed;
}
