using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class SessionKeyValidatorTests
{
    private readonly SessionKeyValidator _validator = new();

    [Fact]
    public void Validate_EmptyString_ReturnsFailure()
    {
        var result = _validator.Validate("");
        Assert.False(result.IsValid);
        Assert.Equal(SessionKeyValidationError.Empty, result.Error);
    }

    [Fact]
    public void Validate_TooShort_ReturnsFailure()
    {
        var result = _validator.Validate("sk-ant-abc");
        Assert.False(result.IsValid);
        Assert.Equal(SessionKeyValidationError.TooShort, result.Error);
    }

    [Fact]
    public void Validate_WrongPrefix_ReturnsFailure()
    {
        var result = _validator.Validate("wrong-prefix-1234567890-abcdefg");
        Assert.False(result.IsValid);
        Assert.Equal(SessionKeyValidationError.InvalidPrefix, result.Error);
    }

    [Fact]
    public void Validate_ContainsWhitespace_ReturnsFailure()
    {
        var result = _validator.Validate("sk-ant-sid01-abc def-ghijklmnop");
        Assert.False(result.IsValid);
        Assert.Equal(SessionKeyValidationError.ContainsWhitespace, result.Error);
    }

    [Fact]
    public void Validate_ValidKey_ReturnsSuccess()
    {
        var result = _validator.Validate("sk-ant-sid01-abcdefghijk-lmnopqrstuvwxyz");
        Assert.True(result.IsValid);
        Assert.NotNull(result.SanitizedKey);
    }

    [Fact]
    public void Validate_PathTraversal_ReturnsFailure()
    {
        var result = _validator.Validate("sk-ant-sid01-abc..def-ghijklmnop");
        Assert.False(result.IsValid);
        Assert.Equal(SessionKeyValidationError.PotentiallyMalicious, result.Error);
    }

    [Fact]
    public void Validate_ScriptInjection_ReturnsFailure()
    {
        var key = "sk-ant-sid01-<script>alert-xss</script>";
        var result = _validator.Validate(key);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_TrimsWhitespace()
    {
        var result = _validator.Validate("  sk-ant-sid01-abcdefghijk-lmnopqrstuvwxyz  ");
        Assert.True(result.IsValid);
        Assert.Equal("sk-ant-sid01-abcdefghijk-lmnopqrstuvwxyz", result.SanitizedKey);
    }

    [Fact]
    public void IsValid_ValidKey_ReturnsTrue()
    {
        Assert.True(_validator.IsValid("sk-ant-sid01-abcdefghijk-lmnopqrstuvwxyz"));
    }

    [Fact]
    public void IsValid_InvalidKey_ReturnsFalse()
    {
        Assert.False(_validator.IsValid("invalid-key"));
    }

    [Fact]
    public void SanitizeForStorage_RemovesNewlines()
    {
        var sanitized = _validator.SanitizeForStorage("sk-ant-test\r\n-key\n");
        Assert.DoesNotContain("\n", sanitized);
        Assert.DoesNotContain("\r", sanitized);
    }
}
