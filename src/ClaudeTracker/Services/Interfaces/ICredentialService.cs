namespace ClaudeTracker.Services.Interfaces;

/// <summary>Manages Claude CLI credential storage via Windows Credential Manager.</summary>
public interface ICredentialService
{
    /// <summary>Reads stored CLI credentials JSON, or null if not found.</summary>
    string? ReadCliCredentials();
    /// <summary>Writes CLI credentials JSON to secure storage.</summary>
    void WriteCliCredentials(string json);
    /// <summary>Removes stored CLI credentials.</summary>
    void DeleteCliCredentials();
    /// <summary>Returns true if CLI credentials exist in storage.</summary>
    bool HasCliCredentials();
}
