namespace ClaudeTracker.Services.Interfaces;

/// <summary>Manages automatic application updates via Velopack and GitHub Releases.</summary>
public interface IUpdateService
{
    /// <summary>Starts periodic background update checks.</summary>
    Task StartAsync();
    /// <summary>Checks for updates. Non-blocking.</summary>
    Task CheckForUpdatesAsync();
    /// <summary>Downloads and applies the pending update, then restarts the app.</summary>
    Task ApplyUpdateAsync();
    /// <summary>Whether the app was installed via Velopack (not a dev build).</summary>
    bool IsInstalled { get; }
    /// <summary>Whether an update is available for download.</summary>
    bool IsUpdateAvailable { get; }
    /// <summary>Version string of the available update, or null.</summary>
    string? AvailableVersion { get; }
    /// <summary>Human-readable status (e.g., "Up to date", "Update available: v1.2.0").</summary>
    string StatusText { get; }
    /// <summary>Whether a check or download is in progress.</summary>
    bool IsBusy { get; }
    /// <summary>Raised when any property changes.</summary>
    event EventHandler? StateChanged;
}
