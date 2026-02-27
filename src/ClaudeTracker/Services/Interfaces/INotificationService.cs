using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface INotificationService
{
    void CheckAndNotify(Profile profile, ClaudeUsage usage);
    void SendNotification(string title, string message);
}
