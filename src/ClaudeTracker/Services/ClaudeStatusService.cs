using System.Net.Http;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeStatusService : IClaudeStatusService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClaudeStatusService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClaudeStatus> FetchStatusAsync()
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetStringAsync(Constants.StatusAPI.StatusUrl);
            using var doc = JsonDocument.Parse(response);
            var statusObj = doc.RootElement.GetProperty("status");
            var indicator = statusObj.GetProperty("indicator").GetString() ?? "unknown";
            var description = statusObj.GetProperty("description").GetString() ?? "Status Unknown";

            return new ClaudeStatus(ClaudeStatus.ParseIndicator(indicator), description);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to fetch Claude status", ex);
            return ClaudeStatus.Unknown;
        }
    }
}
