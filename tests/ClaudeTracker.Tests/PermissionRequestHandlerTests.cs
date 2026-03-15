using System.Text.Json;
using Xunit;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Handlers;

namespace ClaudeTracker.Tests;

public class PermissionRequestHandlerTests
{
    [Fact]
    public void BuildResponseJson_AlwaysAllow_EchoesRawSuggestionVerbatim()
    {
        var rawJson = """{"type":"prefix","behavior":"allow","destination":"session","tool":"Edit","prefix":"src/"}""";
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.AlwaysAllow,
            AppliedSuggestion = new PermissionSuggestion
            {
                Type = "prefix",
                Behavior = "allow",
                Destination = "session",
                Tool = "Edit",
                Prefix = "src/",
                RawJson = rawJson
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
        // Echoes back the original suggestion verbatim
        Assert.Equal("prefix", perm.GetProperty("type").GetString());
        Assert.Equal("Edit", perm.GetProperty("tool").GetString());
        Assert.Equal("session", perm.GetProperty("destination").GetString());
        Assert.Equal("src/", perm.GetProperty("prefix").GetString());
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
    public void BuildResponseJson_AlwaysAllow_BashRule_EchoesRawJson()
    {
        var rawJson = """{"type":"prefix","behavior":"allow","tool":"Bash","rules":[{"toolName":"Bash","ruleContent":"mkdir"}],"directories":["/projects/test"]}""";
        var result = new PermissionDecisionResult
        {
            Decision = PermissionDecision.AlwaysAllow,
            AppliedSuggestion = new PermissionSuggestion
            {
                Type = "prefix",
                Behavior = "allow",
                Tool = "Bash",
                RawJson = rawJson,
                Rules = new List<PermissionRule>
                {
                    new() { ToolName = "Bash", RuleContent = "mkdir" }
                },
                Directories = new List<string> { "/projects/test" }
            }
        };

        var json = PermissionRequestHandler.BuildResponseJson(result);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var decision = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("decision");

        var perms = decision.GetProperty("updatedPermissions");
        var perm = perms[0];
        // Echoes raw JSON verbatim — preserves all fields including rules and directories
        Assert.Equal("prefix", perm.GetProperty("type").GetString());
        Assert.Equal("Bash", perm.GetProperty("tool").GetString());
        var rules = perm.GetProperty("rules");
        Assert.Equal("mkdir", rules[0].GetProperty("ruleContent").GetString());
        Assert.True(perm.TryGetProperty("directories", out _));
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
