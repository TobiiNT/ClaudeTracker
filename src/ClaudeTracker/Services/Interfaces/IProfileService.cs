using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Manages user profiles, each with independent credentials and usage data.</summary>
public interface IProfileService
{
    /// <summary>The currently active profile, or null if none.</summary>
    Profile? ActiveProfile { get; }
    /// <summary>All configured profiles.</summary>
    IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Creates a new profile with an auto-generated funny name.</summary>
    Profile CreateProfile(string? name = null);
    /// <summary>Persists changes to an existing profile.</summary>
    void UpdateProfile(Profile profile);
    /// <summary>Deletes a profile by ID.</summary>
    void DeleteProfile(Guid profileId);
    /// <summary>Switches the active profile and triggers a refresh.</summary>
    void ActivateProfile(Guid profileId);
    /// <summary>Updates cached usage data for a specific profile.</summary>
    void UpdateUsageData(Guid profileId, ClaudeUsage? claudeUsage = null, APIUsage? apiUsage = null);
    /// <summary>Updates personal Claude Code metrics for a specific profile.</summary>
    void UpdatePersonalMetrics(Guid profileId, ClaudeCodeUserMetrics? metrics);
    /// <summary>Sets the Claude organization ID for a profile.</summary>
    void UpdateOrganizationId(string? orgId, Guid profileId);
    /// <summary>Saves credentials (session keys, org IDs) for a profile.</summary>
    void SaveCredentials(Guid profileId, ProfileCredentials credentials);
    /// <summary>Loads credentials for a profile.</summary>
    ProfileCredentials LoadCredentials(Guid profileId);

    /// <summary>Raised when the active profile changes.</summary>
    event EventHandler<Profile?>? ActiveProfileChanged;
    /// <summary>Raised when the profiles list is modified.</summary>
    event EventHandler? ProfilesChanged;
}
