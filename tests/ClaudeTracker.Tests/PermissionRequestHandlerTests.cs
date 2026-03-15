using System.Text.Json;
using Xunit;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Handlers;

namespace ClaudeTracker.Tests;

public class PermissionRequestHandlerTests
{
    [Fact]
    public void BuildResponseJson_AlwaysAllow_EmitsToolAlwaysAllowFormat()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.AlwaysAllow,
            AppliedSuggestion = new PermissionSuggestion
            {
                Type = "prefix",
                Behavior = "allow",
                Destination = "session",
                Tool = "Edit",
                Prefix = "src/"
            }
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");
        Assert.Equal("allow", decision.GetProperty("behavior").GetString());

        var perms = decision.GetProperty("updatedPermissions");
        Assert.Equal(1, perms.GetArrayLength());
        var perm = perms[0];
        Assert.Equal("toolAlwaysAllow", perm.GetProperty("type").GetString());
        Assert.Equal("Edit", perm.GetProperty("tool").GetString());
        Assert.False(perm.TryGetProperty("destination", out _));
        Assert.False(perm.TryGetProperty("prefix", out _));
        Assert.False(perm.TryGetProperty("behavior", out _));
    }

    [Fact]
    public void BuildResponseJson_Allow_NoUpdatedPermissions()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.Allow
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");
        Assert.Equal("allow", decision.GetProperty("behavior").GetString());
        Assert.False(decision.TryGetProperty("updatedPermissions", out _));
    }

    [Fact]
    public void BuildResponseJson_Deny_ReturnsDenyBehavior()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.Deny
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");
        Assert.Equal("deny", decision.GetProperty("behavior").GetString());
    }

    [Fact]
    public void BuildResponseJson_HandleInTerminal_ReturnsNull()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.HandleInTerminal
        };

        Assert.Null(PermissionRequestHandler.BuildResponseJson(result));
    }

    [Fact]
    public void BuildResponseJson_AllowWithUpdatedInput_IncludesUpdatedInput()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.Allow,
            UpdatedInput = new Dictionary<string, object>
            {
                ["command"] = "npm run lint"
            }
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");
        var updatedInput = decision.GetProperty("updatedInput");
        Assert.Equal("npm run lint", updatedInput.GetProperty("command").GetString());
    }

    [Fact]
    public void BuildResponseJson_AlwaysAllow_NoSuggestion_DegradesToAllow()
    {
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.AlwaysAllow,
            AppliedSuggestion = null
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");
        Assert.Equal("allow", decision.GetProperty("behavior").GetString());
        Assert.False(decision.TryGetProperty("updatedPermissions", out _));
    }
}
