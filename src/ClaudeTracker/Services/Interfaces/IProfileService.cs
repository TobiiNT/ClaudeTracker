using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IProfileService
{
    Profile? ActiveProfile { get; }
    IReadOnlyList<Profile> Profiles { get; }

    Profile CreateProfile(string? name = null);
    void UpdateProfile(Profile profile);
    void DeleteProfile(Guid profileId);
    void ActivateProfile(Guid profileId);
    void UpdateUsageData(Guid profileId, ClaudeUsage? claudeUsage = null, APIUsage? apiUsage = null);
    void UpdateOrganizationId(string? orgId, Guid profileId);
    void SaveCredentials(Guid profileId, ProfileCredentials credentials);
    ProfileCredentials LoadCredentials(Guid profileId);

    event EventHandler<Profile?>? ActiveProfileChanged;
    event EventHandler? ProfilesChanged;
}
