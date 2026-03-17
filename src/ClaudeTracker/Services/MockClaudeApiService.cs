using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

public class MockClaudeApiService : IClaudeApiService
{
    private double _simulatedSessionPercentage = 15.0;
    private readonly Random _random = new();

    public Task<List<AccountInfo>> FetchAllOrganizations(string? sessionKey = null)
    {
        return Task.FromResult(new List<AccountInfo>
        {
            new()
            {
                Uuid = "mock-org-001",
                Name = "Mock Organization",
                Capabilities = new List<string> { "chat", "claude_pro" }
            }
        });
    }

    public Task<List<AccountInfo>> TestSessionKey(string key)
    {
        return FetchAllOrganizations(key);
    }

    public Task<string> FetchOrganizationId(string? sessionKey = null)
    {
        return Task.FromResult("mock-org-001");
    }

    public Task<ClaudeUsage> FetchUsageData()
    {
        // Increment session usage by 0.3-0.8% per call
        var increment = 0.3 + _random.NextDouble() * 0.5;
        _simulatedSessionPercentage += increment;

        // Reset when hitting 100%
        if (_simulatedSessionPercentage >= 100)
            _simulatedSessionPercentage = 2.0;

        var usage = new ClaudeUsage
        {
            SessionPercentage = Math.Round(_simulatedSessionPercentage, 1),
            SessionTokensUsed = (int)(_simulatedSessionPercentage / 100.0 * 200_000),
            SessionLimit = 200_000,
            SessionResetTime = DateTime.UtcNow.AddHours(3).AddMinutes(42),
            WeeklyPercentage = 30.5,
            WeeklyTokensUsed = 305_000,
            WeeklyLimit = 1_000_000,
            WeeklyResetTime = DateTime.UtcNow.AddDays(3),
            OpusWeeklyTokensUsed = 180_000,
            OpusWeeklyPercentage = 18.0,
            HasOpusData = true,
            SonnetWeeklyTokensUsed = 125_000,
            SonnetWeeklyPercentage = 12.5,
            HasSonnetData = true,
            LastUpdated = DateTime.UtcNow
        };

        return Task.FromResult(usage);
    }

    public Task<ClaudeUsage> FetchUsageData(string sessionKey, string organizationId)
    {
        return FetchUsageData();
    }

    public Task SendInitializationMessage()
    {
        LoggingService.Instance.Log("[Mock] SendInitializationMessage (no-op)");
        return Task.CompletedTask;
    }

    public Task<List<APIOrganization>> FetchConsoleOrganizations(string apiSessionKey)
    {
        return Task.FromResult(new List<APIOrganization>
        {
            new() { Id = "mock-console-org", Name = "Mock Console Org" }
        });
    }

    public Task<APIUsage> FetchAPIUsageData(string organizationId, string apiSessionKey)
    {
        return Task.FromResult(new APIUsage
        {
            CurrentSpendCents = 1250,
            PrepaidCreditsCents = 8750,
            ResetsAt = DateTime.UtcNow.AddDays(15),
            Currency = "USD"
        });
    }

    public Task<List<ClaudeCodeUserMetrics>> FetchClaudeCodeAllUsers(
        string organizationUuid, string apiSessionKey)
        => Task.FromResult(new List<ClaudeCodeUserMetrics>());

    public Task<ClaudeCodeUserMetrics?> FetchClaudeCodeUserMetrics(
        string organizationUuid, string apiSessionKey, string search)
        => Task.FromResult<ClaudeCodeUserMetrics?>(null);
}
