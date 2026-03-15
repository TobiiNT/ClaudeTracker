using System.IO;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Views;
using static ClaudeTracker.Utilities.Constants.Hooks;

namespace ClaudeTracker.Services.Handlers;

/// <summary>
/// Handles Stop events from Claude Code.
/// Sends a "Task Complete" notification when Claude finishes,
/// if the stop notification preference is enabled.
/// </summary>
public class StopHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;

    public StopHandler(ISettingsService settingsService, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
    }

    public bool CanHandle(string eventName) => eventName == Events.Stop;

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        LoggingService.Instance.Log($"StopHandler: Processing stop event {evt.RequestId}");

        try
        {
            // Check if stop notifications are enabled
            var prefs = _settingsService.Settings.HookNotificationPreferences;
            if (prefs.TryGetValue("stop", out var enabled) && enabled)
            {
                var projectName = ParseProjectName(evt.Payload);
                var title = "Task Complete";
                var message = string.IsNullOrEmpty(projectName)
                    ? "Claude Code has finished processing."
                    : $"Claude Code has finished processing in {projectName}.";

                ((NotificationService)_notificationService).SendNotification(
                    title, message, NotificationPopup.NotificationLevel.Info);

                LoggingService.Instance.Log($"StopHandler: Sent completion notification for project '{projectName}'");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("StopHandler: Failed to send stop notification", ex);
        }

        return Task.FromResult(new HookResponse
        {
            RequestId = evt.RequestId,
            Success = true,
            JsonOutput = null
        });
    }

    private static string ParseProjectName(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return string.Empty;

        try
        {
            var node = JsonNode.Parse(payload);
            var cwd = node?[Fields.Cwd]?.GetValue<string>();

            if (string.IsNullOrEmpty(cwd))
                return string.Empty;

            return Path.GetFileName(cwd) ?? cwd;
        }
        catch
        {
            return string.Empty;
        }
    }
}
