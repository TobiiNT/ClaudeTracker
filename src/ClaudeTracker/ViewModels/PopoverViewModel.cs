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
    [ObservableProperty] private double _sessionElapsedFraction;

    [ObservableProperty] private PaceStatus? _weeklyPaceStatus;
    [ObservableProperty] private string _weeklyPaceLabel = "";
    [ObservableProperty] private string _weeklyPaceColorHex = "#4CAF50";
    [ObservableProperty] private double _weeklyElapsedFraction;

    [ObservableProperty] private string _claudeStatusDescription = "";
    [ObservableProperty] private string _claudeStatusColorHex = "#9E9E9E";
    [ObservableProperty] private bool _showClaudeStatus;

    public ObservableCollection<Profile> Profiles { get; } = new();

    public PopoverViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;

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

            SessionPercentage = usage.EffectiveSessionPercentage;
            SessionPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.EffectiveSessionPercentage, showRemaining));
            SessionResetText = $"Resets {FormatterHelper.FormatTimeRemaining(usage.SessionResetTime)}";
            SessionStatus = UsageStatusCalculator.CalculateStatus(usage.EffectiveSessionPercentage, showRemaining);

            WeeklyPercentage = usage.WeeklyPercentage;
            WeeklyPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.WeeklyPercentage, showRemaining));
            WeeklyResetText = $"Resets {FormatterHelper.FormatTimeRemaining(usage.WeeklyResetTime)}";
            WeeklyStatus = UsageStatusCalculator.CalculateStatus(usage.WeeklyPercentage, showRemaining);

            // Pace calculation
            var sessionElapsed = PaceStatusCalculator.CalculateSessionElapsed(usage.SessionResetTime);
            SessionElapsedFraction = sessionElapsed;
            SessionPaceStatus = PaceStatusCalculator.Calculate(usage.EffectiveSessionPercentage, sessionElapsed);
            if (SessionPaceStatus.HasValue)
            {
                SessionPaceLabel = FormatPaceLabel(SessionPaceStatus.Value);
                SessionPaceColorHex = PaceStatusCalculator.GetColorHex(SessionPaceStatus.Value);
            }
            else
            {
                SessionPaceLabel = "";
            }

            var weeklyElapsed = PaceStatusCalculator.CalculateWeeklyElapsed(usage.WeeklyResetTime);
            WeeklyElapsedFraction = weeklyElapsed;
            WeeklyPaceStatus = PaceStatusCalculator.Calculate(usage.WeeklyPercentage, weeklyElapsed);
            if (WeeklyPaceStatus.HasValue)
            {
                WeeklyPaceLabel = FormatPaceLabel(WeeklyPaceStatus.Value);
                WeeklyPaceColorHex = PaceStatusCalculator.GetColorHex(WeeklyPaceStatus.Value);
            }
            else
            {
                WeeklyPaceLabel = "";
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
}
