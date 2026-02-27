using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IProfileService _profileService;

    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = "";

    public ObservableCollection<Profile> Profiles { get; } = new();
    public Guid? ActiveProfileId => _profileService.ActiveProfile?.Id;

    public ProfilesViewModel(IProfileService profileService)
    {
        _profileService = profileService;
        _profileService.ProfilesChanged += (_, _) => RefreshList();
        RefreshList();
    }

    [RelayCommand]
    private void CreateProfile()
    {
        var name = string.IsNullOrWhiteSpace(NewProfileName) ? null : NewProfileName.Trim();
        _profileService.CreateProfile(name);
        NewProfileName = "";
    }

    [RelayCommand]
    private void ActivateProfile(Guid profileId) => _profileService.ActivateProfile(profileId);

    [RelayCommand]
    private void DeleteProfile(Guid profileId) => _profileService.DeleteProfile(profileId);

    [RelayCommand]
    private void RenameProfile((Guid Id, string Name) args)
    {
        var profile = _profileService.Profiles.FirstOrDefault(p => p.Id == args.Id);
        if (profile != null)
        {
            profile.Name = args.Name;
            _profileService.UpdateProfile(profile);
        }
    }

    private void RefreshList()
    {
        Profiles.Clear();
        foreach (var p in _profileService.Profiles)
            Profiles.Add(p);
        OnPropertyChanged(nameof(ActiveProfileId));
    }
}
