using System.IO;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

public class CredentialService : ICredentialService
{
    private static readonly string CredentialsFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public string? ReadCliCredentials()
    {
        try
        {
            if (!File.Exists(CredentialsFilePath))
            {
                LoggingService.Instance.Log($"CLI credentials file not found: {CredentialsFilePath}");
                return null;
            }

            var json = File.ReadAllText(CredentialsFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            LoggingService.Instance.Log("Read CLI credentials from ~/.claude/.credentials.json");
            return json;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to read CLI credentials file", ex);
            return null;
        }
    }

    public void WriteCliCredentials(string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(CredentialsFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(CredentialsFilePath, json);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to write CLI credentials file", ex);
        }
    }

    public void DeleteCliCredentials()
    {
        try
        {
            if (File.Exists(CredentialsFilePath))
                File.Delete(CredentialsFilePath);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to delete CLI credentials file", ex);
        }
    }

    public bool HasCliCredentials()
    {
        return File.Exists(CredentialsFilePath);
    }
}
