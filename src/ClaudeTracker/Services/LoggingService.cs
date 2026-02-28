using System.IO;
using Serilog;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
    public static LoggingService Instance => _instance.Value;

    private readonly ILogger _logger;

    private LoggingService()
    {
        var logDir = Path.GetDirectoryName(Constants.LogFilePath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Constants.LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _logger.Information("ClaudeTracker logging initialized");
    }

    public void Log(string message) => _logger.Information(message);
    public void LogInfo(string message) => _logger.Information(message);
    public void LogDebug(string message) => _logger.Debug(message);
    public void LogWarning(string message) => _logger.Warning(message);
    public void LogError(string message) => _logger.Error(message);
    public void LogError(string message, Exception ex) => _logger.Error(ex, message);
    public void LogAPIRequest(string endpoint) => _logger.Debug("API Request: {Endpoint}", endpoint);
    public void LogAPIResponse(string endpoint, int statusCode) =>
        _logger.Debug("API Response: {Endpoint} -> {StatusCode}", endpoint, statusCode);
    public void LogAPIError(string endpoint, Exception error) =>
        _logger.Error(error, "API Error: {Endpoint}", endpoint);

    public void LogFatal(string message, Exception ex) => _logger.Fatal(ex, message);

    /// <summary>
    /// Flush all pending log entries to disk. Call before process exit on crash.
    /// </summary>
    public void Flush() => (_logger as IDisposable)?.Dispose();
}
