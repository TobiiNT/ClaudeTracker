using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Load();
    event EventHandler? SettingsChanged;
}
