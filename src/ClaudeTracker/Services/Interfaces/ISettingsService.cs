using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Manages application settings persistence to %APPDATA%/ClaudeTracker/settings.json.</summary>
public interface ISettingsService
{
    /// <summary>The current application settings.</summary>
    AppSettings Settings { get; }
    /// <summary>Persists current settings to disk.</summary>
    void Save();
    /// <summary>Loads settings from disk, creating defaults if not found.</summary>
    void Load();
    /// <summary>Raised after settings are saved or loaded.</summary>
    event EventHandler? SettingsChanged;
}
