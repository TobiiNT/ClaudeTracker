namespace ClaudeTracker.Services.Interfaces;

public interface IUsageRefreshCoordinator
{
    void Start();
    void Stop();
    void RefreshNow();
    void UpdateInterval(double seconds);
    bool IsRunning { get; }

    event EventHandler? RefreshStarted;
    event EventHandler? RefreshCompleted;
    event EventHandler<string>? RefreshFailed;
}
