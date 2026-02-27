using System.Text.RegularExpressions;

namespace ClaudeTracker.Utilities;

public enum SessionKeyValidationError
{
    Empty,
    TooShort,
    TooLong,
    InvalidPrefix,
    InvalidCharacters,
    InvalidFormat,
    ContainsWhitespace,
    PotentiallyMalicious
}

public class SessionKeyValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RecoverySuggestion { get; init; }
    public SessionKeyValidationError? Error { get; init; }
    public string? SanitizedKey { get; init; }

    public static SessionKeyValidationResult Success(string sanitizedKey) => new()
    {
        IsValid = true,
        SanitizedKey = sanitizedKey
    };

    public static SessionKeyValidationResult Failure(SessionKeyValidationError error, string message, string? suggestion = null) => new()
    {
        IsValid = false,
        Error = error,
        ErrorMessage = message,
        RecoverySuggestion = suggestion ?? "Please copy the complete sessionKey cookie value from your browser's DevTools"
    };
}

/// <summary>Validates and sanitizes Claude session keys (sk-ant-* format) with security checks.</summary>
public partial class SessionKeyValidator
{
    private const string RequiredPrefix = "sk-ant-";
    private const int MinLength = 20;
    private const int MaxLength = 500;

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$")]
    private static partial Regex AllowedCharsRegex();

    /// <summary>Validates format, length, prefix, and security of a session key.</summary>
    public SessionKeyValidationResult Validate(string sessionKey)
    {
        var trimmed = sessionKey.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.Empty, "Session key cannot be empty");

        if (trimmed.Any(char.IsWhiteSpace))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.ContainsWhitespace,
                "Session key cannot contain whitespace",
                "Remove any spaces or newlines from the session key");

        if (trimmed.Length < MinLength)
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.TooShort,
                $"Session key too short (minimum: {MinLength}, actual: {trimmed.Length})");

        if (trimmed.Length > MaxLength)
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.TooLong,
                $"Session key too long (maximum: {MaxLength}, actual: {trimmed.Length})");

        if (!trimmed.StartsWith(RequiredPrefix))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.InvalidPrefix,
                $"Session key must start with '{RequiredPrefix}'",
                "Ensure you're copying the sessionKey cookie value, which should start with 'sk-ant-'");

        // Security checks
        var securityResult = PerformSecurityChecks(trimmed);
        if (securityResult != null)
            return securityResult;

        // Character validation
        if (!AllowedCharsRegex().IsMatch(trimmed))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.InvalidCharacters,
                "Session key contains invalid characters",
                "The session key may be corrupted. Please copy it again from your browser");

        // Format validation
        var afterPrefix = trimmed[RequiredPrefix.Length..];
        if (string.IsNullOrEmpty(afterPrefix))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.InvalidFormat,
                "No content after prefix");

        if (!afterPrefix.Contains('-') && !afterPrefix.Contains('_'))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.InvalidFormat,
                "Missing expected separators in session key");

        return SessionKeyValidationResult.Success(trimmed);
    }

    /// <summary>Returns true if the session key passes all validation checks.</summary>
    public bool IsValid(string sessionKey) => Validate(sessionKey).IsValid;

    /// <summary>Strips whitespace and line breaks for safe storage.</summary>
    public string SanitizeForStorage(string sessionKey) =>
        sessionKey.Trim().Replace("\r\n", "").Replace("\n", "").Replace("\r", "");

    private static SessionKeyValidationResult? PerformSecurityChecks(string sessionKey)
    {
        if (sessionKey.Contains('\0'))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.PotentiallyMalicious,
                "Contains null bytes",
                "Please verify the session key is from a legitimate source");

        if (sessionKey.Any(c => char.IsControl(c)))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.PotentiallyMalicious,
                "Contains control characters");

        if (sessionKey.Contains("..") || sessionKey.Contains("//"))
            return SessionKeyValidationResult.Failure(SessionKeyValidationError.PotentiallyMalicious,
                "Contains suspicious patterns");

        string[] suspiciousPatterns = ["<script", "javascript:", "data:", "vbscript:", "file:"];
        foreach (var pattern in suspiciousPatterns)
        {
            if (sessionKey.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return SessionKeyValidationResult.Failure(SessionKeyValidationError.PotentiallyMalicious,
                    "Contains script injection pattern");
        }

        return null;
    }
}
