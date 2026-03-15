using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using ClaudeTracker.Views;

namespace ClaudeTracker.Services;

public class NotificationService : INotificationService
{
    private readonly IProfileService _profileService;
    private readonly HashSet<string> _sentNotifications = new();
    private readonly HashSet<string> _sentExpiryNotifications = new();
    private DateTime _lastNotificationTime = DateTime.MinValue;

    public NotificationService(IProfileService profileService)
    {
        _profileService = profileService;
    }

    public event EventHandler? NotificationClicked;

    public void CheckAndNotify(Profile profile, ClaudeUsage usage)
    {
        if (!profile.NotificationSettings.Enabled) return;

        var percentage = usage.EffectiveSessionPercentage;
        var profileKey = profile.Id.ToString();

        if (percentage >= Constants.NotificationThresholds.Critical
            && profile.NotificationSettings.Threshold95Enabled)
        {
            SendThresholdNotification(profileKey, 95, percentage, profile.Name);
        }
        else if (percentage >= Constants.NotificationThresholds.High
                 && profile.NotificationSettings.Threshold90Enabled)
        {
            SendThresholdNotification(profileKey, 90, percentage, profile.Name);
        }
        else if (percentage >= Constants.NotificationThresholds.Warning
                 && profile.NotificationSettings.Threshold75Enabled)
        {
            SendThresholdNotification(profileKey, 75, percentage, profile.Name);
        }

        // Reset notifications when usage drops below thresholds
        if (percentage < Constants.NotificationThresholds.Warning)
        {
            _sentNotifications.RemoveWhere(k => k.StartsWith(profileKey));
        }
    }

    public void CheckKeyExpiry(Profile profile)
    {
        CheckSingleKeyExpiry(profile, "claude", profile.ClaudeSessionKeyExpiry);
        CheckSingleKeyExpiry(profile, "api", profile.ApiSessionKeyExpiry);
    }

    private void CheckSingleKeyExpiry(Profile profile, string keyType, DateTime? expiry)
    {
        if (!expiry.HasValue) return;

        var remaining = expiry.Value - DateTime.UtcNow;
        if (remaining.TotalHours > 24) return;

        var expiryKey = $"{profile.Id}_expiry_{keyType}";
        if (_sentExpiryNotifications.Contains(expiryKey)) return;

        var title = $"Session Key Expiring — {profile.Name}";
        var message = remaining.TotalHours > 0
            ? $"Your {keyType} session key expires in {(int)remaining.TotalHours}h {remaining.Minutes}m. Re-authenticate to avoid interruption."
            : $"Your {keyType} session key has expired. Re-authenticate to continue tracking.";

        SendNotification(title, message, NotificationPopup.NotificationLevel.Warning);
        _sentExpiryNotifications.Add(expiryKey);
    }

    public void ResetExpiryNotifications(Guid profileId, string keyType)
    {
        _sentExpiryNotifications.Remove($"{profileId}_expiry_{keyType}");
    }

    private void SendThresholdNotification(string profileKey, int threshold, double percentage, string profileName)
    {
        var key = $"{profileKey}_{threshold}";
        if (_sentNotifications.Contains(key)) return;

        // Debounce: minimum 30 seconds between notifications
        if ((DateTime.UtcNow - _lastNotificationTime).TotalSeconds < 30) return;

        var remaining = Math.Max(0, 100 - percentage);
        var title = $"Claude Usage Alert — {profileName}";
        var message = $"Session usage at {percentage:F0}% ({remaining:F0}% remaining)";

        var level = threshold >= 95
            ? NotificationPopup.NotificationLevel.Critical
            : threshold >= 90
                ? NotificationPopup.NotificationLevel.Warning
                : NotificationPopup.NotificationLevel.Info;

        SendNotification(title, message, level);
        _sentNotifications.Add(key);
        _lastNotificationTime = DateTime.UtcNow;
    }

    public void SendNotification(string title, string message,
        NotificationPopup.NotificationLevel level = NotificationPopup.NotificationLevel.Warning)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var popup = new NotificationPopup(title, message, level);
                popup.NotificationClicked += (_, _) => NotificationClicked?.Invoke(this, EventArgs.Empty);
                popup.Show();
            });

            PlayNotificationSound();
            LoggingService.Instance.Log($"Notification sent: {title}");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to send notification", ex);
        }
    }

    private void PlayNotificationSound()
    {
        try
        {
            var profile = _profileService.ActiveProfile;
            if (profile?.NotificationSettings.SoundEnabled != true) return;

            var sound = profile.NotificationSettings.SoundName switch
            {
                "Hand" => System.Media.SystemSounds.Hand,
                "Beep" => System.Media.SystemSounds.Beep,
                "None" => (System.Media.SystemSound?)null,
                _ => System.Media.SystemSounds.Exclamation
            };
            sound?.Play();
        }
        catch { /* ignore sound failures */ }
    }

    // Keep the two-parameter overload for interface compatibility
    void INotificationService.SendNotification(string title, string message)
    {
        SendNotification(title, message);
    }
}
