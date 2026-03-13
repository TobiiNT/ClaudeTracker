using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IClaudeStatusService
{
    Task<ClaudeStatus> FetchStatusAsync();
}
