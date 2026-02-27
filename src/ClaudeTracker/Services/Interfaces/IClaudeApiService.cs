using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Service for communicating with Claude AI and Anthropic Console APIs.</summary>
public interface IClaudeApiService
{
    /// <summary>Fetches all organizations accessible with the given or active session key.</summary>
    Task<List<AccountInfo>> FetchAllOrganizations(string? sessionKey = null);
    /// <summary>Validates a session key by attempting to fetch organizations.</summary>
    Task<List<AccountInfo>> TestSessionKey(string key);
    /// <summary>Fetches the primary organization ID for the active profile.</summary>
    Task<string> FetchOrganizationId(string? sessionKey = null);
    /// <summary>Fetches usage data using the active profile's credentials.</summary>
    Task<ClaudeUsage> FetchUsageData();
    /// <summary>Fetches usage data using explicit session key and organization ID.</summary>
    Task<ClaudeUsage> FetchUsageData(string sessionKey, string organizationId);
    /// <summary>Sends a lightweight message to initialize a Claude session for usage tracking.</summary>
    Task SendInitializationMessage();

    /// <summary>Fetches organizations from the Anthropic Console API.</summary>
    Task<List<APIOrganization>> FetchConsoleOrganizations(string apiSessionKey);
    /// <summary>Fetches API billing/usage data for a Console organization.</summary>
    Task<APIUsage> FetchAPIUsageData(string organizationId, string apiSessionKey);
}
