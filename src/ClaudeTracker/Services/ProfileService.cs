using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ProfileService : IProfileService
{
    private readonly ISettingsService _settingsService;
    private Profile? _activeProfile;

    public Profile? ActiveProfile => _activeProfile;
    public IReadOnlyList<Profile> Profiles => _settingsService.Settings.Profiles.AsReadOnly();

    public event EventHandler<Profile?>? ActiveProfileChanged;
    public event EventHandler? ProfilesChanged;

    public ProfileService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeProfiles();
    }

    private void InitializeProfiles()
    {
        var settings = _settingsService.Settings;

        if (settings.Profiles.Count == 0)
        {
            var defaultProfile = CreateProfile("Default");
            settings.ActiveProfileId = defaultProfile.Id;
            _settingsService.Save();
        }

        if (settings.ActiveProfileId.HasValue)
        {
            _activeProfile = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId.Value);
        }

        _activeProfile ??= settings.Profiles.FirstOrDefault();

        if (_activeProfile != null && settings.ActiveProfileId != _activeProfile.Id)
        {
            settings.ActiveProfileId = _activeProfile.Id;
            _settingsService.Save();
        }
    }

    public Profile CreateProfile(string? name = null)
    {
        var usedNames = _settingsService.Settings.Profiles.Select(p => p.Name);
        var profileName = name ?? FunnyNameGenerator.GetRandomName(usedNames);

        var profile = new Profile
        {
            Name = profileName,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow
        };

        _settingsService.Settings.Profiles.Add(profile);
        _settingsService.Save();

        LoggingService.Instance.Log($"Created profile '{profile.Name}' (ID: {profile.Id})");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        return profile;
    }

    public void UpdateProfile(Profile profile)
    {
        var index = _settingsService.Settings.Profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _settingsService.Settings.Profiles[index] = profile;
            _settingsService.Save();

            if (_activeProfile?.Id == profile.Id)
            {
                _activeProfile = profile;
                ActiveProfileChanged?.Invoke(this, _activeProfile);
            }

            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteProfile(Guid profileId)
    {
        if (_settingsService.Settings.Profiles.Count <= 1)
        {
            LoggingService.Instance.LogWarning("Cannot delete the last profile");
            return;
        }

        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        _settingsService.Settings.Profiles.Remove(profile);

        if (_activeProfile?.Id == profileId)
        {
            _activeProfile = _settingsService.Settings.Profiles.FirstOrDefault();
            _settingsService.Settings.ActiveProfileId = _activeProfile?.Id;
            ActiveProfileChanged?.Invoke(this, _activeProfile);
        }

        _settingsService.Save();
        LoggingService.Instance.Log($"Deleted profile '{profile.Name}'");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActivateProfile(Guid profileId)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        profile.LastUsedAt = DateTime.UtcNow;
        _activeProfile = profile;
        _settingsService.Settings.ActiveProfileId = profileId;
        _settingsService.Save();

        LoggingService.Instance.Log($"Activated profile '{profile.Name}'");
        ActiveProfileChanged?.Invoke(this, _activeProfile);
    }

    public void UpdateUsageData(Guid profileId, ClaudeUsage? claudeUsage = null, APIUsage? apiUsage = null)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        if (claudeUsage != null) profile.ClaudeUsage = claudeUsage;
        if (apiUsage != null) profile.ApiUsage = apiUsage;

        _settingsService.Save();

        if (_activeProfile?.Id == profileId)
        {
            _activeProfile = profile;
            ActiveProfileChanged?.Invoke(this, _activeProfile);
        }
    }

    public void UpdatePersonalMetrics(Guid profileId, ClaudeCodeUserMetrics? metrics)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        profile.PersonalMetrics = metrics;
        _settingsService.Save();

        if (_activeProfile?.Id == profileId)
        {
            _activeProfile = profile;
            ActiveProfileChanged?.Invoke(this, _activeProfile);
        }
    }

    public void UpdateOrganizationId(string? orgId, Guid profileId)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        profile.OrganizationId = orgId;
        _settingsService.Save();

        if (_activeProfile?.Id == profileId)
            _activeProfile = profile;
    }

    public void SaveCredentials(Guid profileId, ProfileCredentials credentials)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        profile.ClaudeSessionKey = credentials.ClaudeSessionKey;
        profile.OrganizationId = credentials.OrganizationId;
        profile.ApiSessionKey = credentials.ApiSessionKey;
        profile.ApiOrganizationId = credentials.ApiOrganizationId;
        profile.CliCredentialsJSON = credentials.CliCredentialsJSON;

        _settingsService.Save();

        if (_activeProfile?.Id == profileId)
        {
            _activeProfile = profile;
            ActiveProfileChanged?.Invoke(this, _activeProfile);
        }
    }

    public ProfileCredentials LoadCredentials(Guid profileId)
    {
        var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return new ProfileCredentials();

        return new ProfileCredentials
        {
            ClaudeSessionKey = profile.ClaudeSessionKey,
            OrganizationId = profile.OrganizationId,
            ApiSessionKey = profile.ApiSessionKey,
            ApiOrganizationId = profile.ApiOrganizationId,
            CliCredentialsJSON = profile.CliCredentialsJSON
        };
    }
}
