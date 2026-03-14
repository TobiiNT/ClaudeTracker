namespace ClaudeTracker.Services.Interfaces;

public interface INetworkMonitorService : IDisposable
{
    void Start();
    event EventHandler? NetworkRestored;
}
