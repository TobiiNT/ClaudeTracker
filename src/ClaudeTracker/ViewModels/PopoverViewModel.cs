using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.ViewModels;

public partial class PopoverViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private string _profileName = "Default";
    [ObservableProperty] private string _sessionPercentageText = "0%";
    [ObservableProperty] private double _sessionPercentage;
    [ObservableProperty] private string _sessionResetText = "";
    [ObservableProperty] private string _weeklyPercentageText = "0%";
    [ObservableProperty] private double _weeklyPercentage;
    [ObservableProperty] private string _weeklyResetText = "";
    [ObservableProperty] private string _opusPercentageText = "0%";
    [ObservableProperty] private double _opusPercentage;
    [ObservableProperty] private string _sonnetPercentageText = "0%";
    [ObservableProperty] private double _sonnetPercentage;
    [ObservableProperty] private bool _hasModelData;
    [ObservableProperty] private bool _hasCostData;
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private bool _hasApiUsage;
    [ObservableProperty] private string _apiUsedText = "";
    [ObservableProperty] private string _apiRemainingText = "";
    [ObservableProperty] private string _apiTotalText = "";
    [ObservableProperty] private double _apiPercentage;
    [ObservableProperty] private bool _hasPersonalMetrics;
    [ObservableProperty] private string _personalCostText = "";
    [ObservableProperty] private string _personalAvgCostText = "";
    [ObservableProperty] private string _personalSessionsText = "";
    [ObservableProperty] private string _personalLinesText = "";
    [ObservableProperty] private bool _hasDailyMetrics;
    [ObservableProperty] private string _dailyCostText = "";
    [ObservableProperty] private string _dailySessionsText = "";
    [ObservableProperty] private string _dailyLinesText = "";
    [ObservableProperty] private string _lastUpdatedText = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasCredentials;
    [ObservableProperty] private bool _hasClaudeUsage;
    [ObservableProperty] private UsageStatusLevel _sessionStatus = UsageStatusLevel.Safe;
    [ObservableProperty] private UsageStatusLevel _weeklyStatus = UsageStatusLevel.Safe;

    [ObservableProperty] private PaceStatus? _sessionPaceStatus;
    [ObservableProperty] private string _sessionPaceLabel = "";
    [ObservableProperty] private string _sessionPaceColorHex = "#4CAF50";
    [ObservableProperty] private string _sessionPaceTooltip = "";
    [ObservableProperty] private string _sessionEstimateText = "";
    [ObservableProperty] private double _sessionElapsedFraction;

    [ObservableProperty] private PaceStatus? _weeklyPaceStatus;
    [ObservableProperty] private string _weeklyPaceLabel = "";
    [ObservableProperty] private string _weeklyPaceColorHex = "#4CAF50";
    [ObservableProperty] private string _weeklyPaceTooltip = "";
    [ObservableProperty] private string _weeklyEstimateText = "";
    [ObservableProperty] private double _weeklyElapsedFraction;

    [ObservableProperty] private string _claudeStatusDescription = "";
    [ObservableProperty] private string _claudeStatusColorHex = "#9E9E9E";
    [ObservableProperty] private bool _showClaudeStatus;

    [ObservableProperty] private int _activeSessionCount;
    [ObservableProperty] private bool _hasActiveSessions;
    [ObservableProperty] private bool _showActivityFeed;
    [ObservableProperty] private bool _isActivityFeedExpanded = true;

    public ObservableCollection<Profile> Profiles { get; } = new();
    public ObservableCollection<SessionState> ActiveSessions { get; } = new();
    public ObservableCollection<ActivityEntry> ActivityFeed { get; } = new();

    public PopoverViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ISettingsService settingsService,
        ISessionTrackingService sessionTracking,
        IActivityService activityService)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;
        _settingsService = settingsService;

        _profileService.ActiveProfileChanged += (_, _) => { UpdateProfilesList(); RefreshData(); };
        _profileService.ProfilesChanged += (_, _) => UpdateProfilesList();
        _refreshCoordinator.RefreshStarted += (_, _) => IsRefreshing = true;
        _refreshCoordinator.RefreshCompleted += (_, _) =>
        {
            IsRefreshing = false;
            RefreshData();
        };
        _refreshCoordinator.RefreshFailed += (_, error) =>
        {
            IsRefreshing = false;
            LastUpdatedText = error.Contains("Rate limited", StringComparison.OrdinalIgnoreCase)
                ? "Rate limited — try again later"
                : "Update failed";
        };

        sessionTracking.SessionsChanged += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ActiveSessions.Clear();
                foreach (var s in sessionTracking.ActiveSessions)
                    ActiveSessions.Add(s);
                ActiveSessionCount = sessionTracking.ActiveSessionCount;
                HasActiveSessions = ActiveSessionCount > 0;
            });
        };

        activityService.RecentFeed.CollectionChanged += (_, _) =>
        {
            // Don't track new events if feed is disabled
            if (!_settingsService.Settings.HookActivityFeedEnabled) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ActivityFeed.Clear();
                foreach (var e in activityService.RecentFeed)
                    ActivityFeed.Add(e);
            });
        };

        // Listen for settings changes to update feed visibility immediately
        settingsService.SettingsChanged += (_, _) =>
        {
            ShowActivityFeed = settingsService.Settings.HookActivityFeedEnabled;
            if (!ShowActivityFeed)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => ActivityFeed.Clear());
            }
        };

        ShowActivityFeed = settingsService.Settings.HookActivityFeedEnabled;

        UpdateProfilesList();
        RefreshData();
    }

    [RelayCommand]
    private void Refresh() => _refreshCoordinator.RefreshNow();

    [RelayCommand]
    private void SwitchProfile(Guid profileId) => _profileService.ActivateProfile(profileId);

    public void RefreshData()
    {
        // Re-read activity feed setting (may have changed in settings)
        ShowActivityFeed = _settingsService.Settings.HookActivityFeedEnabled;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        ProfileName = profile.Name;
        HasCredentials = profile.HasUsageCredentials;

        var usage = profile.HasClaudeAI ? profile.ClaudeUsage : null;
        HasClaudeUsage = usage != null;
        if (usage != null)
        {
            var showRemaining = profile.IconConfig.ShowRemainingPercentage;
            var settings = _settingsService.Settings;
            var use24Hour = settings.TimeFormatPreference switch
            {
                TimeFormatPreference.TwelveHour => false,
                TimeFormatPreference.TwentyFourHour => true,
                _ => FormatterHelper.IsSystem24Hour()
            };
            var timeDisplay = settings.PopoverTimeDisplay;

            SessionPercentage = usage.EffectiveSessionPercentage;
            SessionPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.EffectiveSessionPercentage, showRemaining));
            SessionResetText = FormatResetText(usage.SessionResetTime, timeDisplay, use24Hour);
            SessionStatus = UsageStatusCalculator.CalculateStatus(usage.EffectiveSessionPercentage, showRemaining);

            WeeklyPercentage = usage.WeeklyPercentage;
            WeeklyPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.WeeklyPercentage, showRemaining));
            WeeklyResetText = FormatResetText(usage.WeeklyResetTime, timeDisplay, use24Hour);
            WeeklyStatus = UsageStatusCalculator.CalculateStatus(usage.WeeklyPercentage, showRemaining);

            // Pace calculation — hide when at limit (100%)
            TimeSpan? sessionEta = null;
            if (usage.EffectiveSessionPercentage >= 99.5)
            {
                // At limit — show critical indicator, hide pace estimate
                SessionPaceLabel = "Limit reached";
                SessionPaceColorHex = "#F44336";
                SessionEstimateText = "";
                SessionPaceTooltip = $"Usage limit reached. Resets at {usage.SessionResetTime:HH:mm}";
                SessionPaceStatus = null;
            }
            else
            {
                var sessionElapsed = PaceStatusCalculator.CalculateSessionElapsed(usage.SessionResetTime);
                SessionElapsedFraction = sessionElapsed;
                SessionPaceStatus = PaceStatusCalculator.Calculate(usage.EffectiveSessionPercentage, sessionElapsed);
                if (SessionPaceStatus.HasValue)
                {
                    SessionPaceLabel = FormatPaceLabel(SessionPaceStatus.Value);
                    SessionPaceColorHex = PaceStatusCalculator.GetColorHex(SessionPaceStatus.Value);
                    sessionEta = PaceStatusCalculator.EstimateTimeToLimit(
                        usage.EffectiveSessionPercentage, sessionElapsed, usage.SessionResetTime);
                    var sessionWillExceed = PaceStatusCalculator.WillExceedBeforeReset(sessionEta, usage.SessionResetTime);
                    SessionEstimateText = ShouldShowEstimateInline(SessionPaceStatus.Value, sessionWillExceed)
                        ? FormatEstimate(sessionEta) : "";
                    SessionPaceTooltip = FormatPaceTooltip(SessionPaceStatus.Value, sessionEta,
                        usage.SessionResetTime, isWeekly: false);
                }
                else
                {
                    SessionPaceLabel = "";
                    SessionPaceTooltip = "";
                    SessionEstimateText = "";
                }
            }

            var weeklyElapsed = PaceStatusCalculator.CalculateWeeklyElapsed(usage.WeeklyResetTime);
            WeeklyElapsedFraction = weeklyElapsed;
            WeeklyPaceStatus = PaceStatusCalculator.Calculate(usage.WeeklyPercentage, weeklyElapsed);
            if (WeeklyPaceStatus.HasValue)
            {
                WeeklyPaceLabel = FormatPaceLabel(WeeklyPaceStatus.Value);
                WeeklyPaceColorHex = PaceStatusCalculator.GetColorHex(WeeklyPaceStatus.Value);
                var weeklyEta = PaceStatusCalculator.EstimateTimeToLimit(
                    usage.WeeklyPercentage, weeklyElapsed, usage.WeeklyResetTime);
                // Weekly runout cannot be before session runout
                if (weeklyEta.HasValue && sessionEta.HasValue && weeklyEta.Value < sessionEta.Value)
                    weeklyEta = sessionEta;
                var weeklyWillExceed = PaceStatusCalculator.WillExceedBeforeReset(weeklyEta, usage.WeeklyResetTime);
                WeeklyEstimateText = ShouldShowEstimateInline(WeeklyPaceStatus.Value, weeklyWillExceed)
                    ? FormatEstimate(weeklyEta) : "";
                WeeklyPaceTooltip = FormatPaceTooltip(WeeklyPaceStatus.Value, weeklyEta,
                    usage.WeeklyResetTime, isWeekly: true);
            }
            else
            {
                WeeklyPaceLabel = "";
                WeeklyPaceTooltip = "";
                WeeklyEstimateText = "";
            }

            OpusPercentage = usage.OpusWeeklyPercentage;
            OpusPercentageText = FormatterHelper.FormatPercentage(usage.OpusWeeklyPercentage);

            SonnetPercentage = usage.SonnetWeeklyPercentage;
            SonnetPercentageText = FormatterHelper.FormatPercentage(usage.SonnetWeeklyPercentage);

            HasModelData = (usage.HasOpusData && usage.OpusWeeklyPercentage > 0)
                        || (usage.HasSonnetData && usage.SonnetWeeklyPercentage > 0);

            HasCostData = usage.CostUsed.HasValue && usage.CostLimit.HasValue;
            if (HasCostData)
                CostText = $"${usage.CostUsed:F2} / ${usage.CostLimit:F2} {usage.CostCurrency}";

            LastUpdatedText = $"Updated {FormatterHelper.FormatTimeAgo(usage.LastUpdated)}";
        }
        else
        {
            SessionPercentage = 0;
            SessionPercentageText = "\u2014";
            SessionResetText = "";
            WeeklyPercentage = 0;
            WeeklyPercentageText = "\u2014";
            WeeklyResetText = "";
            HasModelData = false;
            HasCostData = false;
            LastUpdatedText = "";
            SessionPaceStatus = null;
            SessionPaceLabel = "";
            SessionElapsedFraction = 0;
            WeeklyPaceStatus = null;
            WeeklyPaceLabel = "";
            WeeklyElapsedFraction = 0;
        }

        // Claude system status
        var status = _refreshCoordinator.CurrentStatus;
        ClaudeStatusDescription = status.Description;
        ClaudeStatusColorHex = ClaudeStatus.GetColorHex(status.Indicator);
        ShowClaudeStatus = status.Indicator != StatusIndicator.None
                          && status.Indicator != StatusIndicator.Unknown;

        var apiUsage = profile.ApiUsage;
        HasApiUsage = apiUsage != null;
        if (apiUsage != null)
        {
            ApiUsedText = apiUsage.FormattedUsed;
            ApiRemainingText = apiUsage.FormattedRemaining;
            ApiTotalText = apiUsage.FormattedTotal;
            ApiPercentage = apiUsage.UsagePercentage;

            // For API-only users (no subscription), show last updated from API data
            if (!HasClaudeUsage)
                LastUpdatedText = $"Updated {FormatterHelper.FormatTimeAgo(apiUsage.LastUpdated)}";
        }

        var personalMetrics = profile.PersonalMetrics;
        HasPersonalMetrics = personalMetrics != null;
        if (personalMetrics != null)
        {
            PersonalCostText = personalMetrics.FormattedTotalCost;
            PersonalAvgCostText = personalMetrics.FormattedAvgCostPerDay;
            PersonalSessionsText = $"{personalMetrics.TotalSessions} sessions";
            PersonalLinesText = $"{personalMetrics.TotalLinesAccepted:N0} lines";
        }

        var dailyMetrics = profile.DailyMetrics;
        HasDailyMetrics = dailyMetrics != null;
        if (dailyMetrics != null)
        {
            DailyCostText = dailyMetrics.FormattedTotalCost;
            DailySessionsText = $"{dailyMetrics.TotalSessions} sessions";
            DailyLinesText = $"{dailyMetrics.TotalLinesAccepted:N0} lines";
        }
    }

    private void UpdateProfilesList()
    {
        var source = _profileService.Profiles;
        LoggingService.Instance.Log(
            $"PopoverVM.UpdateProfilesList: source={source.Count}, collection={Profiles.Count}");
        Profiles.Clear();
        foreach (var p in source)
            Profiles.Add(p);
    }

    private static string FormatResetText(DateTime resetTime, PopoverTimeDisplay displayMode, bool use24Hour)
    {
        return displayMode switch
        {
            PopoverTimeDisplay.ResetTime => $"Resets {FormatterHelper.FormatResetTimeAbsolute(resetTime, use24Hour)}",
            PopoverTimeDisplay.Both => $"Resets {FormatterHelper.FormatResetTimeCombined(resetTime, use24Hour)}",
            _ => $"Resets {FormatterHelper.FormatTimeRemaining(resetTime)}"
        };
    }

    private static string FormatPaceLabel(PaceStatus pace)
    {
        return pace switch
        {
            PaceStatus.Comfortable => "Comfortable",
            PaceStatus.OnTrack     => "On Track",
            PaceStatus.Warming     => "Warming",
            PaceStatus.Pressing    => "Pressing",
            PaceStatus.Critical    => "Critical",
            PaceStatus.Runaway     => "Runaway",
            _                      => ""
        };
    }

    /// <summary>Only show the ETA inline for paces that will actually hit the limit.</summary>
    private static bool ShouldShowEstimateInline(PaceStatus pace, bool willExceed)
    {
        // Show inline estimate only for urgent paces that will exceed before reset
        return willExceed && pace >= PaceStatus.Pressing;
    }

    private static string FormatPaceTooltip(PaceStatus pace, TimeSpan? eta, DateTime resetTime, bool isWeekly = false)
    {
        var description = pace switch
        {
            PaceStatus.Comfortable => "Well under budget — you have plenty of usage left",
            PaceStatus.OnTrack     => "Sustainable pace — on track to stay within limits",
            PaceStatus.Warming     => "Starting to push it — usage is picking up",
            PaceStatus.Pressing    => "Likely to hit the limit at this pace",
            PaceStatus.Critical    => "On track to exceed your usage limit",
            PaceStatus.Runaway     => "Burning through usage much faster than the reset window",
            _                      => ""
        };

        if (eta.HasValue)
        {
            var willExceed = PaceStatusCalculator.WillExceedBeforeReset(eta, resetTime);
            if (willExceed)
            {
                var rounded = RoundTimeSpanTo15Min(eta.Value);
                var absoluteText = FormatRunoutAbsolute(rounded, isWeekly);
                return $"{description}\nEst. 100% in ~{FormatTimeSpan(rounded)} (~{absoluteText})";
            }
            return $"{description}\nWon't hit limit before reset";
        }

        return description;
    }

    private static string FormatEstimate(TimeSpan? eta)
    {
        if (!eta.HasValue) return "";
        return $"~{FormatTimeSpan(eta.Value)} to limit";
    }

    /// <summary>
    /// Rounds a TimeSpan to the nearest 15 minutes.
    /// E.g. 3h 22m → 3h 15m, 3h 38m → 3h 45m
    /// </summary>
    private static TimeSpan RoundTimeSpanTo15Min(TimeSpan ts)
    {
        var totalMinutes = ts.TotalMinutes;
        var rounded = Math.Round(totalMinutes / 15.0) * 15;
        return TimeSpan.FromMinutes(Math.Max(15, rounded));
    }

    /// <summary>
    /// Session: 24h format — "15:45" or "tomorrow 08:30"
    /// Weekly same day: hours like session — "15:45"
    /// Weekly different day: day of week — "Friday" or "Next Tuesday"
    /// </summary>
    private static string FormatRunoutAbsolute(TimeSpan roundedEta, bool isWeekly)
    {
        var now = DateTime.Now;
        var runoutTime = now.Add(roundedEta);

        if (!isWeekly)
        {
            if (runoutTime.Date == now.Date)
                return runoutTime.ToString("H:mm");
            return $"tomorrow {runoutTime.ToString("H:mm")}";
        }

        // Weekly: same day → show hours like session
        if (runoutTime.Date == now.Date)
            return runoutTime.ToString("H:mm");

        // Weekly: different day → show day of week
        var daysAway = (runoutTime.Date - now.Date).Days;
        var dow = runoutTime.ToString("dddd");
        return daysAway >= 7 ? $"Next {dow}" : dow;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            var days = (int)ts.TotalDays;
            var hours = ts.Hours;
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }
        if (ts.TotalHours >= 1)
        {
            var hours = (int)ts.TotalHours;
            var minutes = ts.Minutes;
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }
        return $"{Math.Max(1, (int)ts.TotalMinutes)}m";
    }
}
