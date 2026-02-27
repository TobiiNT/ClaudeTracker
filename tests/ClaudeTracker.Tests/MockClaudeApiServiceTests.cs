using ClaudeTracker.Services;
using Xunit;

namespace ClaudeTracker.Tests;

public class MockClaudeApiServiceTests
{
    [Fact]
    public async Task FetchUsageData_ReturnsValidUsage()
    {
        var service = new MockClaudeApiService();
        var usage = await service.FetchUsageData();

        Assert.True(usage.SessionPercentage > 0);
        Assert.True(usage.SessionPercentage <= 100);
        Assert.True(usage.WeeklyPercentage > 0);
        Assert.True(usage.SessionResetTime > DateTime.UtcNow);
    }

    [Fact]
    public async Task FetchUsageData_Increments_OnEachCall()
    {
        var service = new MockClaudeApiService();
        var first = await service.FetchUsageData();
        var second = await service.FetchUsageData();

        Assert.True(second.SessionPercentage > first.SessionPercentage);
    }

    [Fact]
    public async Task FetchUsageData_ResetsAt100Percent()
    {
        var service = new MockClaudeApiService();

        // Call enough times to exceed 100%
        double lastPercentage = 0;
        bool didReset = false;
        for (int i = 0; i < 250; i++)
        {
            var usage = await service.FetchUsageData();
            if (usage.SessionPercentage < lastPercentage)
            {
                didReset = true;
                break;
            }
            lastPercentage = usage.SessionPercentage;
        }

        Assert.True(didReset, "Mock service should reset after reaching 100%");
    }

    [Fact]
    public async Task FetchAllOrganizations_ReturnsNonEmpty()
    {
        var service = new MockClaudeApiService();
        var orgs = await service.FetchAllOrganizations();

        Assert.NotEmpty(orgs);
        Assert.NotEmpty(orgs[0].Uuid);
        Assert.NotEmpty(orgs[0].Name);
    }

    [Fact]
    public async Task FetchAPIUsageData_ReturnsValidData()
    {
        var service = new MockClaudeApiService();
        var usage = await service.FetchAPIUsageData("org", "key");

        Assert.True(usage.CurrentSpendCents > 0);
        Assert.True(usage.PrepaidCreditsCents > 0);
        Assert.Equal("USD", usage.Currency);
    }

    [Fact]
    public async Task SendInitializationMessage_DoesNotThrow()
    {
        var service = new MockClaudeApiService();
        await service.SendInitializationMessage();
    }
}
