using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

/// <summary>Reads and writes permission rules from Claude Code's ~/.claude/settings.json.</summary>
public class ClaudeCodeSettingsService : IClaudeCodeSettingsService
{
    private static readonly object _fileLock = new();

    public List<PermissionRuleEntry> GetPermissions()
    {
        var path = Constants.Hooks.ClaudeSettingsPath;
        if (!File.Exists(path)) return new();

        string json;
        lock (_fileLock)
        {
            json = File.ReadAllText(path);
        }
        return ParsePermissions(json);
    }

    public void RemovePermission(string key)
    {
        var path = Constants.Hooks.ClaudeSettingsPath;
        if (!File.Exists(path)) return;

        lock (_fileLock)
        {
            var json = File.ReadAllText(path);
            var updated = RemovePermission(json, key);
            File.WriteAllText(path, updated);
        }
    }

    public static List<PermissionRuleEntry> ParsePermissions(string json)
    {
        var rules = new List<PermissionRuleEntry>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("permissions", out var perms))
            return rules;

        foreach (var prop in perms.EnumerateObject())
        {
            var entry = new PermissionRuleEntry
            {
                Key = prop.Name,
                RawJson = prop.Value.GetRawText()
            };

            if (prop.Value.TryGetProperty("tool", out var tool))
                entry.Tool = tool.GetString() ?? "";
            if (prop.Value.TryGetProperty("type", out var type))
                entry.Type = type.GetString() ?? "";

            entry.DisplayLabel = BuildDisplayLabel(prop.Value, entry);

            rules.Add(entry);
        }

        return rules;
    }

    public static string RemovePermission(string json, string key)
    {
        var node = JsonNode.Parse(json)!;
        var perms = node["permissions"]?.AsObject();
        perms?.Remove(key);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildDisplayLabel(JsonElement value, PermissionRuleEntry entry)
    {
        if (value.TryGetProperty("rules", out var rules) && rules.GetArrayLength() > 0)
        {
            var rule = rules[0];
            var toolName = rule.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "";
            var ruleContent = rule.TryGetProperty("ruleContent", out var rc) ? rc.GetString() ?? "" : "";

            if (!string.IsNullOrEmpty(ruleContent))
            {
                if (toolName == "Bash")
                {
                    var cmd = ruleContent.TrimStart();
                    var firstWord = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() ?? cmd;
                    return $"Allow {firstWord}:*";
                }
                var content = ruleContent.Length > 40 ? ruleContent[..37] + "..." : ruleContent;
                return $"Allow {content}";
            }

            if (!string.IsNullOrEmpty(toolName))
            {
                if (toolName.StartsWith("mcp__"))
                {
                    var parts = toolName[5..].Split("__", 2);
                    if (parts.Length == 2)
                        return $"Allow {parts[0]}: {parts[1]}";
                }
                return $"Allow {toolName}";
            }
        }

        if (!string.IsNullOrEmpty(entry.Tool))
            return $"Allow {entry.Tool}";

        return entry.Key;
    }
}
