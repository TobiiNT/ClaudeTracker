using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ClaudeTracker.Tests;

/// <summary>
/// Tests the JSON manipulation logic used by HookBridge install/uninstall
/// to ensure existing hooks from other tools are preserved.
/// </summary>
public class HookBridgeInstallTests
{
    private const string ClaudeTrackerCommand = "D:/Projects/ClaudeTracker/ClaudeTracker.HookBridge.exe";
    private const string OtherToolCommand = "/usr/local/bin/other-tool";

    private static readonly string[] AllEvents =
    {
        "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest", "Notification", "Stop",
        "SessionStart", "SessionEnd", "UserPromptSubmit",
        "SubagentStart", "SubagentStop",
        "PreCompact", "PostCompact",
        "WorktreeCreate", "WorktreeRemove",
        "InstructionsLoaded", "ConfigChange",
        "Elicitation", "ElicitationResult",
        "TeammateIdle", "TaskCompleted"
    };

    /// <summary>Simulates the install logic from HookBridge Program.cs</summary>
    private static JsonObject SimulateInstall(JsonObject settings, string exePath = ClaudeTrackerCommand)
    {
        if (settings["hooks"] is not JsonObject hooksObj)
        {
            hooksObj = new JsonObject();
            settings["hooks"] = hooksObj;
        }

        foreach (var eventName in AllEvents)
        {
            var hookConfig = new JsonObject
            {
                ["type"] = "command",
                ["command"] = exePath
            };

            var hookEntry = new JsonObject
            {
                ["hooks"] = new JsonArray { hookConfig }
            };

            if (hooksObj[eventName] is JsonArray existingArray)
            {
                // Remove existing ClaudeTracker entries (avoid duplicates)
                for (int i = existingArray.Count - 1; i >= 0; i--)
                {
                    var entryJson = existingArray[i]?.ToJsonString() ?? "";
                    if (entryJson.Contains("ClaudeTracker.HookBridge", StringComparison.OrdinalIgnoreCase))
                        existingArray.RemoveAt(i);
                }
                existingArray.Add(hookEntry);
            }
            else
            {
                hooksObj[eventName] = new JsonArray { hookEntry };
            }
        }

        return settings;
    }

    /// <summary>Simulates the uninstall logic from HookBridge Program.cs</summary>
    private static JsonObject SimulateUninstall(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooksObj)
            return settings;

        var emptyKeys = new List<string>();
        foreach (var kvp in hooksObj)
        {
            if (kvp.Value is not JsonArray eventArray) continue;

            for (int i = eventArray.Count - 1; i >= 0; i--)
            {
                var entryJson = eventArray[i]?.ToJsonString() ?? "";
                if (entryJson.Contains("ClaudeTracker.HookBridge", StringComparison.OrdinalIgnoreCase))
                    eventArray.RemoveAt(i);
            }

            if (eventArray.Count == 0)
                emptyKeys.Add(kvp.Key);
        }

        foreach (var key in emptyKeys)
            hooksObj.Remove(key);

        if (hooksObj.Count == 0)
            settings.Remove("hooks");

        return settings;
    }

    [Fact]
    public void Install_CreatesAllEventHooks()
    {
        var settings = new JsonObject();
        SimulateInstall(settings);

        var hooks = settings["hooks"]!.AsObject();
        Assert.Equal(AllEvents.Length, hooks.Count);

        foreach (var eventName in AllEvents)
        {
            Assert.True(hooks.ContainsKey(eventName), $"Missing hook for {eventName}");
            var arr = hooks[eventName]!.AsArray();
            Assert.True(arr.Count >= 1);
        }
    }

    [Fact]
    public void Install_PreservesExistingHooksFromOtherTools()
    {
        var settings = new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["PostToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = OtherToolCommand
                            }
                        }
                    }
                }
            }
        };

        SimulateInstall(settings);

        var postToolUseArray = settings["hooks"]!["PostToolUse"]!.AsArray();

        // Should have BOTH: other-tool + ClaudeTracker
        Assert.Equal(2, postToolUseArray.Count);

        var allJson = postToolUseArray.ToJsonString();
        Assert.Contains(OtherToolCommand, allJson);
        Assert.Contains(ClaudeTrackerCommand, allJson);
    }

    [Fact]
    public void Install_NoDuplicatesOnReinstall()
    {
        var settings = new JsonObject();
        SimulateInstall(settings);
        SimulateInstall(settings); // Re-install

        var preToolUseArray = settings["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Single(preToolUseArray);
    }

    [Fact]
    public void Install_ReinstallPreservesOtherToolHooks()
    {
        // Start with other-tool's hook
        var settings = new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = OtherToolCommand
                            }
                        }
                    }
                }
            }
        };

        SimulateInstall(settings);
        SimulateInstall(settings); // Re-install

        var arr = settings["hooks"]!["PreToolUse"]!.AsArray();

        // 2 entries: other-tool + ClaudeTracker (no duplicate ClaudeTracker)
        Assert.Equal(2, arr.Count);
        var json = arr.ToJsonString();
        Assert.Contains(OtherToolCommand, json);
        Assert.Contains(ClaudeTrackerCommand, json);
    }

    [Fact]
    public void Uninstall_RemovesOnlyClaudeTrackerHooks()
    {
        var settings = new JsonObject();
        SimulateInstall(settings);

        // Add another tool's hook to PostToolUse
        var postToolUseArray = settings["hooks"]!["PostToolUse"]!.AsArray();
        postToolUseArray.Add(new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = OtherToolCommand
                }
            }
        });

        SimulateUninstall(settings);

        // hooks object should still exist (has other-tool's PostToolUse)
        Assert.NotNull(settings["hooks"]);
        var hooks = settings["hooks"]!.AsObject();

        // Only PostToolUse should remain (with other-tool)
        Assert.Single(hooks);
        Assert.True(hooks.ContainsKey("PostToolUse"));

        var remaining = hooks["PostToolUse"]!.AsArray();
        Assert.Single(remaining);
        Assert.Contains(OtherToolCommand, remaining[0]!.ToJsonString());
        Assert.DoesNotContain("ClaudeTracker", remaining[0]!.ToJsonString());
    }

    [Fact]
    public void Uninstall_RemovesHooksObjectWhenEmpty()
    {
        var settings = new JsonObject();
        SimulateInstall(settings);
        SimulateUninstall(settings);

        // hooks object should be removed entirely
        Assert.Null(settings["hooks"]);
    }

    [Fact]
    public void Install_PreservesNonHookSettings()
    {
        var settings = new JsonObject
        {
            ["permissions"] = new JsonObject { ["allow"] = new JsonArray { (JsonNode)"Read" } },
            ["theme"] = "dark"
        };

        SimulateInstall(settings);

        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.NotNull(settings["permissions"]);
        Assert.NotNull(settings["hooks"]);
    }

    [Fact]
    public void Uninstall_PreservesNonHookSettings()
    {
        var settings = new JsonObject
        {
            ["theme"] = "dark",
            ["permissions"] = new JsonObject()
        };

        SimulateInstall(settings);
        SimulateUninstall(settings);

        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.NotNull(settings["permissions"]);
    }

    [Fact]
    public void Uninstall_NoHooksObject_DoesNotCrash()
    {
        var settings = new JsonObject { ["theme"] = "dark" };
        SimulateUninstall(settings);
        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
    }
}
