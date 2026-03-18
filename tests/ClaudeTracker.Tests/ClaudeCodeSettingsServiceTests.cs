using System.Text.Json;
using Xunit;
using ClaudeTracker.Services;

namespace ClaudeTracker.Tests;

public class ClaudeCodeSettingsServiceTests
{
    [Fact]
    public void ParsePermissions_ReturnsRulesFromJson()
    {
        var json = """
        {
            "permissions": {
                "Allow tool: Edit": { "type": "toolAlwaysAllow", "tool": "Edit" },
                "Allow Bash: git *": { "type": "prefix", "behavior": "allow", "tool": "Bash", "rules": [{"toolName":"Bash","ruleContent":"git"}] }
            },
            "hooks": {}
        }
        """;

        var rules = ClaudeCodeSettingsService.ParsePermissions(json);

        Assert.Equal(2, rules.Count);
        Assert.Equal("Allow tool: Edit", rules[0].Key);
        Assert.Equal("Edit", rules[0].Tool);
        Assert.Equal("Allow Bash: git *", rules[1].Key);
        Assert.Equal("Bash", rules[1].Tool);
    }

    [Fact]
    public void ParsePermissions_EmptyPermissions_ReturnsEmpty()
    {
        var json = """{ "hooks": {} }""";
        var rules = ClaudeCodeSettingsService.ParsePermissions(json);
        Assert.Empty(rules);
    }

    [Fact]
    public void RemovePermission_RemovesKeyFromJson()
    {
        var json = """
        {
            "permissions": {
                "Allow tool: Edit": { "type": "toolAlwaysAllow", "tool": "Edit" },
                "Allow Bash: git *": { "type": "prefix", "tool": "Bash" }
            },
            "hooks": {}
        }
        """;

        var result = ClaudeCodeSettingsService.RemovePermission(json, "Allow tool: Edit");

        using var doc = JsonDocument.Parse(result);
        var perms = doc.RootElement.GetProperty("permissions");
        Assert.False(perms.TryGetProperty("Allow tool: Edit", out _));
        Assert.True(perms.TryGetProperty("Allow Bash: git *", out _));
        Assert.True(doc.RootElement.TryGetProperty("hooks", out _));
    }

    [Fact]
    public void ParsePermissions_ExtractsDisplayLabel()
    {
        var json = """
        {
            "permissions": {
                "rule1": { "type": "toolAlwaysAllow", "tool": "Read" }
            }
        }
        """;

        var rules = ClaudeCodeSettingsService.ParsePermissions(json);
        Assert.Single(rules);
        Assert.False(string.IsNullOrWhiteSpace(rules[0].DisplayLabel));
    }
}
