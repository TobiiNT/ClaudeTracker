using System.Collections.ObjectModel;
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IActivityService
{
    ObservableCollection<ActivityEntry> RecentFeed { get; }
    void Record(ActivityEntry entry);
    void Clear();
}
