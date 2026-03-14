using System.Collections.ObjectModel;
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
    [ObservableProperty] private double _apiPercentage;
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

    public ObservableCollection<Profile> Profiles { get; } = new();

    public PopoverViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ISettingsService settingsService)
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

        UpdateProfilesList();
        RefreshData();
    }

    [RelayCommand]
    private void Refresh() => _refreshCoordinator.RefreshNow();

    [RelayCommand]
    private void SwitchProfile(Guid profileId) => _profileService.ActivateProfile(profileId);

    public void RefreshData()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        ProfileName = profile.Name;
        HasCredentials = profile.HasUsageCredentials;

        var usage = profile.ClaudeUsage;
        HasClaudeUsage = usage != null;
        if (usage != null)
        {
            var showRemaining = profile.IconConfig.ShowRemainingPercentage;
            var settings = _settingsService.Settings;
            var use24Hour = settings.TimeFormatPreference switch
            {
                "twelveHour" => false,
                "twentyFourHour" => true,
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

            // Pace calculation
            var sessionElapsed = PaceStatusCalculator.CalculateSessionElapsed(usage.SessionResetTime);
            SessionElapsedFraction = sessionElapsed;
            SessionPaceStatus = PaceStatusCalculator.Calculate(usage.EffectiveSessionPercentage, sessionElapsed);
            if (SessionPaceStatus.HasValue)
            {
                SessionPaceLabel = FormatPaceLabel(SessionPaceStatus.Value);
                SessionPaceColorHex = PaceStatusCalculator.GetColorHex(SessionPaceStatus.Value);
                var sessionEta = PaceStatusCalculator.EstimateTimeToLimit(
                    usage.EffectiveSessionPercentage, sessionElapsed, usage.SessionResetTime);
                SessionEstimateText = FormatEstimate(sessionEta);
                SessionPaceTooltip = FormatPaceTooltip(SessionPaceStatus.Value, sessionEta);
            }
            else
            {
                SessionPaceLabel = "";
                SessionPaceTooltip = "";
                SessionEstimateText = "";
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
                WeeklyEstimateText = FormatEstimate(weeklyEta);
                WeeklyPaceTooltip = FormatPaceTooltip(WeeklyPaceStatus.Value, weeklyEta);
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
        ShowClaudeStatus = status.Indicator != StatusIndicator.None;

        var apiUsage = profile.ApiUsage;
        HasApiUsage = apiUsage != null;
        if (apiUsage != null)
        {
            ApiUsedText = apiUsage.FormattedUsed;
            ApiRemainingText = apiUsage.FormattedRemaining;
            ApiPercentage = apiUsage.UsagePercentage;
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

    private static string FormatResetText(DateTime resetTime, string displayMode, bool use24Hour)
    {
        return displayMode switch
        {
            "resetTime" => $"Resets {FormatterHelper.FormatResetTimeAbsolute(resetTime, use24Hour)}",
            "both" => $"Resets {FormatterHelper.FormatResetTimeCombined(resetTime, use24Hour)}",
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

    private static string FormatPaceTooltip(PaceStatus pace, TimeSpan? eta)
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
            return $"{description}\nEst. 100% in ~{FormatTimeSpan(eta.Value)}";

        return description;
    }

    private static string FormatEstimate(TimeSpan? eta)
    {
        if (!eta.HasValue) return "";
        return $"~{FormatTimeSpan(eta.Value)} to limit";
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
