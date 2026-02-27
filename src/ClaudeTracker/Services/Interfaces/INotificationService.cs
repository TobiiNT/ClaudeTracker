using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Handles Windows toast notifications for usage threshold alerts.</summary>
public interface INotificationService
{
    /// <summary>Checks usage against configured thresholds and sends a notification if triggered.</summary>
    void CheckAndNotify(Profile profile, ClaudeUsage usage);
    /// <summary>Sends a Windows toast notification with the given title and message.</summary>
    void SendNotification(string title, string message);
}
