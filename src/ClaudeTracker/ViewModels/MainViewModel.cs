using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;

    [ObservableProperty]
    private Profile? _activeProfile;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string? _lastError;

    public MainViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator)
    {
        _profileService = profileService;
        _refreshCoordinator = refreshCoordinator;

        ActiveProfile = _profileService.ActiveProfile;

        _profileService.ActiveProfileChanged += (_, profile) => ActiveProfile = profile;
        _refreshCoordinator.RefreshStarted += (_, _) => IsRefreshing = true;
        _refreshCoordinator.RefreshCompleted += (_, _) =>
        {
            IsRefreshing = false;
            LastError = null;
        };
        _refreshCoordinator.RefreshFailed += (_, error) =>
        {
            IsRefreshing = false;
            LastError = error;
        };
    }

    [RelayCommand]
    private void RefreshNow() => _refreshCoordinator.RefreshNow();
}
