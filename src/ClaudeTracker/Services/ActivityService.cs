using System.Collections.ObjectModel;
using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

/// <summary>
/// Maintains the recent activity feed as an ObservableCollection,
/// trimming to the configured maximum number of entries.
/// All mutations are dispatched to the UI thread.
/// </summary>
public class ActivityService : IActivityService
{
    private readonly ISettingsService _settingsService;

    public ObservableCollection<ActivityEntry> RecentFeed { get; } = new();

    public ActivityService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Record(ActivityEntry entry)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RecentFeed.Insert(0, entry);
            var max = _settingsService.Settings.HookMaxFeedEntries;
            while (RecentFeed.Count > max)
                RecentFeed.RemoveAt(RecentFeed.Count - 1);
        });
    }

    public void Clear()
    {
        Application.Current?.Dispatcher.Invoke(() => RecentFeed.Clear());
    }
}
