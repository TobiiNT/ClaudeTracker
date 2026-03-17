using System.Text.Json;
using ClaudeTracker.Models;
using Xunit;

namespace ClaudeTracker.Tests;

public class ClaudeCodeMetricsTests
{
    [Fact]
    public void ClaudeCodeUserMetrics_Deserialize_FromApiResponse()
    {
        var json = """
        {
            "email": "user@example.com",
            "api_key_name": null,
            "status": "active",
            "avg_cost_per_day": "80.93",
            "avg_lines_accepted_per_day": 1423,
            "total_cost": "971.18",
            "total_lines_accepted": 17076,
            "total_sessions": 51,
            "last_active": "2026-03-17T00:00:00",
            "prs_with_cc": 3,
            "total_prs": 10,
            "prs_with_cc_percentage": 30.0
        }
        """;

        var metrics = JsonSerializer.Deserialize<ClaudeCodeUserMetrics>(json);

        Assert.NotNull(metrics);
        Assert.Equal("user@example.com", metrics.Email);
        Assert.Equal(971.18, metrics.TotalCostUsd, 2);
        Assert.Equal(17076, metrics.TotalLinesAccepted);
        Assert.Equal(51, metrics.TotalSessions);
        Assert.Equal(80.93, metrics.AvgCostPerDayUsd, 2);
        Assert.Equal("$971.18", metrics.FormattedTotalCost);
        Assert.Equal("$80.93/day", metrics.FormattedAvgCostPerDay);
    }

    [Fact]
    public void ClaudeCodeUserMetrics_Deserialize_ApiKeyUser()
    {
        var json = """
        {
            "email": null,
            "api_key_name": "my-claude-key",
            "status": "active",
            "avg_cost_per_day": "5.50",
            "avg_lines_accepted_per_day": 200,
            "total_cost": "44.00",
            "total_lines_accepted": 1600,
            "total_sessions": 10,
            "last_active": "2026-03-17T00:00:00",
            "prs_with_cc": 0,
            "total_prs": 0,
            "prs_with_cc_percentage": 0
        }
        """;

        var metrics = JsonSerializer.Deserialize<ClaudeCodeUserMetrics>(json);

        Assert.NotNull(metrics);
        Assert.Equal("my-claude-key", metrics.DisplayName);
    }

    [Fact]
    public void ClaudeCodeMetricsResponse_Deserialize_WithPagination()
    {
        var json = """
        {
            "organization_id": "org-123",
            "start_date": "2026-03-01",
            "end_date": "2026-04-01",
            "total_users": 1,
            "users": [{
                "email": "user@example.com",
                "api_key_name": null,
                "status": "active",
                "avg_cost_per_day": "10.00",
                "avg_lines_accepted_per_day": 500,
                "total_cost": "100.00",
                "total_lines_accepted": 5000,
                "total_sessions": 20,
                "last_active": "2026-03-17T00:00:00",
                "prs_with_cc": 0,
                "total_prs": 0,
                "prs_with_cc_percentage": 0
            }],
            "pagination": { "limit": 1, "offset": 0, "total": 1, "has_next": false }
        }
        """;

        var response = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

        Assert.NotNull(response);
        Assert.Single(response.Users);
        Assert.Equal(1, response.TotalUsers);
    }
}
