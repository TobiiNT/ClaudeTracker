using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IClaudeApiService
{
    Task<List<AccountInfo>> FetchAllOrganizations(string? sessionKey = null);
    Task<List<AccountInfo>> TestSessionKey(string key);
    Task<string> FetchOrganizationId(string? sessionKey = null);
    Task<ClaudeUsage> FetchUsageData();
    Task<ClaudeUsage> FetchUsageData(string sessionKey, string organizationId);
    Task SendInitializationMessage();

    // Console API
    Task<List<APIOrganization>> FetchConsoleOrganizations(string apiSessionKey);
    Task<APIUsage> FetchAPIUsageData(string organizationId, string apiSessionKey);
}
