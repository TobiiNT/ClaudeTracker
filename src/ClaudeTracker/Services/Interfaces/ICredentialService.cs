namespace ClaudeTracker.Services.Interfaces;

public interface ICredentialService
{
    string? ReadCliCredentials();
    void WriteCliCredentials(string json);
    void DeleteCliCredentials();
    bool HasCliCredentials();
}
