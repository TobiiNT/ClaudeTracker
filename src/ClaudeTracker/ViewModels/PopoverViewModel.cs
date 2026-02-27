using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
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
    [ObservableProperty] private UsageStatusLevel _sessionStatus = UsageStatusLevel.Safe;
    [ObservableProperty] private UsageStatusLevel _weeklyStatus = UsageStatusLevel.Safe;

    public ObservableCollection<Profile> Profiles { get; } = new();

    public PopoverViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;

        _profileService.ActiveProfileChanged += (_, _) => RefreshData();
        _profileService.ProfilesChanged += (_, _) => UpdateProfilesList();
        _refreshCoordinator.RefreshStarted += (_, _) => IsRefreshing = true;
        _refreshCoordinator.RefreshCompleted += (_, _) =>
        {
            IsRefreshing = false;
            RefreshData();
        };
        _refreshCoordinator.RefreshFailed += (_, _) => IsRefreshing = false;

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
        if (usage != null)
        {
            var showRemaining = profile.IconConfig.ShowRemainingPercentage;

            SessionPercentage = usage.SessionPercentage;
            SessionPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.SessionPercentage, showRemaining));
            SessionResetText = $"Resets {FormatterHelper.FormatTimeRemaining(usage.SessionResetTime)}";
            SessionStatus = UsageStatusCalculator.CalculateStatus(usage.SessionPercentage, showRemaining);

            WeeklyPercentage = usage.WeeklyPercentage;
            WeeklyPercentageText = FormatterHelper.FormatPercentage(
                UsageStatusCalculator.GetDisplayPercentage(usage.WeeklyPercentage, showRemaining));
            WeeklyResetText = $"Resets {FormatterHelper.FormatTimeRemaining(usage.WeeklyResetTime)}";
            WeeklyStatus = UsageStatusCalculator.CalculateStatus(usage.WeeklyPercentage, showRemaining);

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
        Profiles.Clear();
        foreach (var p in _profileService.Profiles)
            Profiles.Add(p);
    }
}
