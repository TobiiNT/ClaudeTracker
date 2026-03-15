# Claude Code Hooks Integration v2 — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Full coverage of all 21 Claude Code hook events with real-time activity monitoring, interactive permission/elicitation popups, and configurable notifications.

**Architecture:** Hybrid generic transport + typed handlers. HookBridge is a thin named-pipe relay (no event knowledge). HookEventDispatcher routes events to typed IHookEventHandler implementations for interactive events, and broadcasts all events to IHookEventObserver implementations (ActivityRecorder, SessionTracker). New branch `feat/hooks-v2` from `main`.

**Tech Stack:** .NET 8, WPF, named pipes (System.IO.Pipes), System.Text.Json, CommunityToolkit.Mvvm, MaterialDesignThemes

**Reference:** Commit `6a5d523` on `feat/hooks-integration` (read-only patterns/inspiration, not code reuse)

---

## Task 1: Create Branch + Foundation Models

**Files:**
- Create: `src/ClaudeTracker/Models/HookModels.cs`
- Modify: `src/ClaudeTracker/Utilities/Constants.cs:89` (add Hooks constants)
- Modify: `src/ClaudeTracker/Models/AppSettings.cs:72` (add hooks settings)

**Step 1: Create the feature branch**

```bash
git checkout main
git checkout -b feat/hooks-v2
```

**Step 2: Add Hooks constants to Constants.cs**

Add after `StatusAPI` class (line 88) in `src/ClaudeTracker/Utilities/Constants.cs`:

```csharp
    public static class Hooks
    {
        public static string PipeName => $"ClaudeTracker-Hooks-{Environment.UserName}";
        public const int MaxConcurrentConnections = 10;
        public const int MaxMessageSize = 5 * 1024 * 1024; // 5 MB
        public const int ConnectionTimeoutMs = 3000;
        public const int ResponseTimeoutMs = 310_000; // 310s (above Claude's 300s permission timeout)
        public const int StaleSessionMinutes = 15;
        public const int DefaultMaxActivityEntries = 200;
        public const int DefaultMaxFeedEntries = 10;

        public static string ClaudeSettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        // All 21 hook event names
        public static readonly string[] AllEvents =
        {
            "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionRequest", "Notification", "Stop",
            "SessionStart", "SessionEnd",
            "UserPromptSubmit",
            "SubagentStart", "SubagentStop",
            "PreCompact", "PostCompact",
            "WorktreeCreate", "WorktreeRemove",
            "InstructionsLoaded", "ConfigChange",
            "Elicitation", "ElicitationResult",
            "TeammateIdle", "TaskCompleted"
        };

        // Events that are fire-and-forget (use async: true in hook config)
        public static readonly HashSet<string> AsyncEvents = new()
        {
            "PostToolUse", "PostToolUseFailure",
            "SessionStart", "SessionEnd",
            "SubagentStart", "InstructionsLoaded",
            "PreCompact", "PostCompact",
            "WorktreeRemove", "ElicitationResult"
        };

        // Events that need a response (blocking)
        public static readonly HashSet<string> InteractiveEvents = new()
        {
            "PermissionRequest", "PreToolUse", "Elicitation",
            "UserPromptSubmit", "Stop", "SubagentStop", "ConfigChange"
        };
    }
```

**Step 3: Add hooks settings to AppSettings.cs**

Add after line 72 (`TimeFormatPreference` property) in `src/ClaudeTracker/Models/AppSettings.cs`:

```csharp
    // ── Hooks Integration ──

    [JsonPropertyName("hooksEnabled")]
    public bool HooksEnabled { get; set; }

    [JsonPropertyName("hookPermissionPopupsEnabled")]
    public bool HookPermissionPopupsEnabled { get; set; } = true;

    [JsonPropertyName("hookElicitationPopupsEnabled")]
    public bool HookElicitationPopupsEnabled { get; set; } = true;

    [JsonPropertyName("hookActivityFeedEnabled")]
    public bool HookActivityFeedEnabled { get; set; } = true;

    [JsonPropertyName("hookMaxFeedEntries")]
    public int HookMaxFeedEntries { get; set; } = 10;

    [JsonPropertyName("hookNotificationPreferences")]
    public Dictionary<string, bool> HookNotificationPreferences { get; set; } = new()
    {
        ["stop"] = true,
        ["toolError"] = true,
        ["permission"] = true,
        ["idle"] = true,
        ["configChange"] = false,
        ["sessionLifecycle"] = false,
        ["subagent"] = false
    };
```

**Step 4: Create HookModels.cs**

Create `src/ClaudeTracker/Models/HookModels.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

// ═══════════════════════════════════════════════════════════
// Generic IPC envelope — no enum, string event names
// ═══════════════════════════════════════════════════════════

/// <summary>IPC envelope sent from HookBridge to ClaudeTracker over named pipe.</summary>
public class HookEvent
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = "";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>IPC response sent from ClaudeTracker back to HookBridge.</summary>
public class HookResponse
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("jsonOutput")]
    public string? JsonOutput { get; set; }
}

// ═══════════════════════════════════════════════════════════
// Activity tracking
// ═══════════════════════════════════════════════════════════

public enum ActivityIcon { Tool, Permission, Session, Subagent, System, Notification }

/// <summary>Universal log record for all hook events — displayed in activity feed.</summary>
public class ActivityEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = "";
    public string EventName { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = "";
    public ActivityIcon Icon { get; set; }
    public string? ToolName { get; set; }
    public string RawPayload { get; set; } = "";
}

// ═══════════════════════════════════════════════════════════
// Session state
// ═══════════════════════════════════════════════════════════

/// <summary>Tracks an active Claude Code session with activity history.</summary>
public class SessionState
{
    public string SessionId { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string PermissionMode { get; set; } = "";
    public string? Model { get; set; }
    public int ToolCallCount { get; set; }
    public int SubagentCount { get; set; }
    public string? CurrentActivity { get; set; }
    public ObservableCollection<ActivityEntry> Activities { get; } = new();
    public List<string> ActiveSubagents { get; } = new();

    public string ProjectName => System.IO.Path.GetFileName(ProjectDirectory.TrimEnd('/', '\\'));
}

// ═══════════════════════════════════════════════════════════
// Typed models for interactive handlers
// ═══════════════════════════════════════════════════════════

/// <summary>Parsed PermissionRequest event from Claude Code.</summary>
public class PermissionRequestInfo
{
    public string RequestId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public Dictionary<string, object?> ToolInput { get; set; } = new();
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string PermissionMode { get; set; } = "";
    public List<PermissionSuggestion> PermissionSuggestions { get; set; } = new();
}

public class PermissionSuggestion
{
    public string Type { get; set; } = "";
    public string Behavior { get; set; } = "";
    public string Destination { get; set; } = "";
    public List<PermissionRule> Rules { get; set; } = new();
    public List<string> Directories { get; set; } = new();
    public string Tool { get; set; } = "";
    public string Prefix { get; set; } = "";

    public string DisplayLabel
    {
        get
        {
            if (Rules.Count > 0)
            {
                var rule = Rules[0];
                return string.IsNullOrEmpty(rule.RuleContent)
                    ? $"Always Allow {rule.ToolName}"
                    : $"Always Allow {rule.ToolName}({rule.RuleContent})";
            }
            if (Directories.Count > 0)
                return $"Allow access to {string.Join(", ", Directories)}";
            if (!string.IsNullOrEmpty(Tool))
                return $"Always Allow {Tool}";
            return "Always Allow";
        }
    }
}

public class PermissionRule
{
    public string ToolName { get; set; } = "";
    public string RuleContent { get; set; } = "";
}

public enum PermissionDecision { Allow, Deny, HandleInTerminal, AlwaysAllow }

public class PermissionDecisionResult
{
    public PermissionDecision Decision { get; set; }
    public PermissionSuggestion? AppliedSuggestion { get; set; }
    public Dictionary<string, object?>? UpdatedInput { get; set; }
}

/// <summary>Parsed PreToolUse event.</summary>
public class PreToolUseInfo
{
    public string ToolName { get; set; } = "";
    public Dictionary<string, object?> ToolInput { get; set; } = new();
    public string? ToolUseId { get; set; }
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
}

public class PreToolUseResult
{
    public string? PermissionDecision { get; set; } // "allow" | "deny" | "ask" | null (passthrough)
    public Dictionary<string, object?>? UpdatedInput { get; set; }
    public string? AdditionalContext { get; set; }
}

/// <summary>Parsed Elicitation event (MCP server requests user input).</summary>
public class ElicitationInfo
{
    public string McpServerName { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Mode { get; set; }
    public string? Url { get; set; }
    public string ElicitationId { get; set; } = "";
    public string? RequestedSchema { get; set; } // JSON schema string
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
}

public class ElicitationDecisionResult
{
    public string Action { get; set; } = "deny"; // "allow" | "deny"
    public Dictionary<string, object?>? Content { get; set; }
}

/// <summary>Parsed UserPromptSubmit event.</summary>
public class UserPromptSubmitInfo
{
    public string Prompt { get; set; } = "";
    public string SessionId { get; set; } = "";
}

/// <summary>Parsed Stop event.</summary>
public class StopInfo
{
    public string? LastAssistantMessage { get; set; }
    public bool StopHookActive { get; set; }
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
}

/// <summary>Parsed SubagentStop event.</summary>
public class SubagentStopInfo
{
    public string AgentId { get; set; } = "";
    public string? AgentType { get; set; }
    public string? LastAssistantMessage { get; set; }
    public string SessionId { get; set; } = "";
}

/// <summary>Parsed ConfigChange event.</summary>
public class ConfigChangeInfo
{
    public string Source { get; set; } = "";
    public string? FilePath { get; set; }
    public string SessionId { get; set; } = "";
}

/// <summary>EventArgs for interactive events that require async UI response.</summary>
public class HookInteractiveEventArgs<T> : EventArgs
{
    public required T Info { get; init; }
    public required TaskCompletionSource<HookResponse> ResponseSource { get; init; }
}
```

**Step 5: Build to verify compilation**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/ClaudeTracker/Models/HookModels.cs src/ClaudeTracker/Utilities/Constants.cs src/ClaudeTracker/Models/AppSettings.cs
git commit -m "feat(hooks-v2): add foundation models, constants, and settings"
```

---

## Task 2: HookBridge Console App

**Files:**
- Create: `src/ClaudeTracker.HookBridge/ClaudeTracker.HookBridge.csproj`
- Create: `src/ClaudeTracker.HookBridge/Program.cs`
- Modify: `ClaudeTracker.sln` (add project)

**Step 1: Create the HookBridge project**

```bash
dotnet new console -n ClaudeTracker.HookBridge -o src/ClaudeTracker.HookBridge --framework net8.0
dotnet sln ClaudeTracker.sln add src/ClaudeTracker.HookBridge/ClaudeTracker.HookBridge.csproj
```

**Step 2: Set up the project file**

Overwrite `src/ClaudeTracker.HookBridge/ClaudeTracker.HookBridge.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>ClaudeTracker.HookBridge</AssemblyName>
    <RootNamespace>ClaudeTracker.HookBridge</RootNamespace>
  </PropertyGroup>
</Project>
```

**Step 3: Write Program.cs — generic relay**

Create `src/ClaudeTracker.HookBridge/Program.cs`:

```csharp
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeTracker.HookBridge;

public static class Program
{
    private const int ConnectionTimeoutMs = 3000;
    private const int ResponseTimeoutMs = 310_000;

    private static string PipeName => $"ClaudeTracker-Hooks-{Environment.UserName}";

    private static string ClaudeSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    // All 21 Claude Code hook events
    private static readonly string[] AllEvents =
    {
        "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest", "Notification", "Stop",
        "SessionStart", "SessionEnd",
        "UserPromptSubmit",
        "SubagentStart", "SubagentStop",
        "PreCompact", "PostCompact",
        "WorktreeCreate", "WorktreeRemove",
        "InstructionsLoaded", "ConfigChange",
        "Elicitation", "ElicitationResult",
        "TeammateIdle", "TaskCompleted"
    };

    // Events that should not block Claude Code
    private static readonly HashSet<string> AsyncEvents = new()
    {
        "PostToolUse", "PostToolUseFailure",
        "SessionStart", "SessionEnd",
        "SubagentStart", "InstructionsLoaded",
        "PreCompact", "PostCompact",
        "WorktreeRemove", "ElicitationResult"
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0)
            {
                return args[0].ToLowerInvariant() switch
                {
                    "install" => Install(),
                    "uninstall" => Uninstall(),
                    "status" => await Status(),
                    _ => ShowHelp()
                };
            }

            return await HandleHookEvent();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClaudeTracker.HookBridge error: {ex.Message}");
            return 0; // Exit 0 so Claude Code falls back gracefully
        }
    }

    private static async Task<int> HandleHookEvent()
    {
        var input = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        // Extract event name from Claude Code JSON — that's all we parse
        var json = JsonNode.Parse(input);
        var eventName = json?["hook_event_name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(eventName))
            return 0;

        // Build generic IPC envelope
        var message = JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid().ToString(),
            eventName,
            payload = input,
            timestamp = DateTime.UtcNow.ToString("O")
        });

        // Connect to ClaudeTracker named pipe
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            pipe.Connect(ConnectionTimeoutMs);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return 0; // ClaudeTracker not running — silent fallback
        }

        // Send length-prefixed message
        var payload = Encoding.UTF8.GetBytes(message);
        await pipe.WriteAsync(BitConverter.GetBytes(payload.Length));
        await pipe.WriteAsync(payload);
        await pipe.FlushAsync();

        // Read length-prefixed response
        using var cts = new CancellationTokenSource(ResponseTimeoutMs);

        var lengthBuf = new byte[4];
        if (await ReadExactAsync(pipe, lengthBuf, cts.Token) < 4)
            return 0;

        var responseLength = BitConverter.ToInt32(lengthBuf, 0);
        if (responseLength <= 0 || responseLength > 1024 * 1024)
            return 0;

        var responseBuf = new byte[responseLength];
        if (await ReadExactAsync(pipe, responseBuf, cts.Token) < responseLength)
            return 0;

        var response = JsonNode.Parse(Encoding.UTF8.GetString(responseBuf));
        var jsonOutput = response?["jsonOutput"]?.GetValue<string>();
        if (jsonOutput != null)
            Console.Write(jsonOutput);

        return 0;
    }

    private static int Install()
    {
        var hookBridgePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeTracker.HookBridge.exe");

        var settingsPath = ClaudeSettingsPath;
        var settingsDir = Path.GetDirectoryName(settingsPath)!;
        if (!Directory.Exists(settingsDir))
            Directory.CreateDirectory(settingsDir);

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            var existing = File.ReadAllText(settingsPath);
            settings = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            settings = new JsonObject();
        }

        var command = $"\"{hookBridgePath}\"";

        if (settings["hooks"] is not JsonObject hooksObj)
        {
            hooksObj = new JsonObject();
            settings["hooks"] = hooksObj;
        }

        foreach (var evt in AllEvents)
        {
            var hookConfig = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command
            };

            // Non-interactive events run async (don't block Claude)
            if (AsyncEvents.Contains(evt))
                hookConfig["async"] = true;

            // SessionEnd has short timeout (Claude caps at 1.5s)
            if (evt == "SessionEnd")
                hookConfig["timeout"] = 2;

            // SessionStart matches "startup" and "resume"
            JsonObject entryObj;
            if (evt == "SessionStart")
            {
                entryObj = new JsonObject
                {
                    ["matcher"] = "startup|resume",
                    ["hooks"] = new JsonArray { hookConfig }
                };
            }
            else
            {
                entryObj = new JsonObject
                {
                    ["hooks"] = new JsonArray { hookConfig }
                };
            }

            hooksObj[evt] = new JsonArray { entryObj };
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(settingsPath, settings.ToJsonString(options));

        Console.WriteLine($"Hooks installed to {settingsPath}");
        Console.WriteLine($"Registered {AllEvents.Length} hook events");
        Console.WriteLine($"Hook command: {command}");
        return 0;
    }

    private static int Uninstall()
    {
        var settingsPath = ClaudeSettingsPath;
        if (!File.Exists(settingsPath))
        {
            Console.WriteLine("No Claude settings found.");
            return 0;
        }

        var settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
        if (settings?["hooks"] is not JsonObject hooksObj)
        {
            Console.WriteLine("No hooks configured.");
            return 0;
        }

        // Remove only entries containing our HookBridge command
        var toRemove = new List<string>();
        foreach (var (key, value) in hooksObj)
        {
            if (value?.ToJsonString().Contains("ClaudeTracker.HookBridge") == true)
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
            hooksObj.Remove(key);

        if (hooksObj.Count == 0)
            settings.Remove("hooks");

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Removed {toRemove.Count} hook registrations.");
        return 0;
    }

    private static async Task<int> Status()
    {
        var installed = File.Exists(ClaudeSettingsPath) &&
            File.ReadAllText(ClaudeSettingsPath).Contains("ClaudeTracker.HookBridge");
        Console.WriteLine($"Hooks: {(installed ? "installed" : "not installed")}");

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(1000);
            Console.WriteLine("IPC: connected (ClaudeTracker is running)");
        }
        catch
        {
            Console.WriteLine("IPC: not reachable (ClaudeTracker may not be running)");
        }

        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("ClaudeTracker.HookBridge — Claude Code hooks relay");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClaudeTracker.HookBridge install     Register all hook events");
        Console.WriteLine("  ClaudeTracker.HookBridge uninstall   Remove hook registrations");
        Console.WriteLine("  ClaudeTracker.HookBridge status      Check installation and IPC status");
        Console.WriteLine();
        Console.WriteLine("Without arguments: reads hook JSON from stdin, relays to ClaudeTracker.");
        return 0;
    }

    private static async ValueTask<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
```

**Step 4: Build both projects**

Run: `dotnet build ClaudeTracker.sln --configuration Release`
Expected: Build succeeded (2 projects)

**Step 5: Commit**

```bash
git add src/ClaudeTracker.HookBridge/ ClaudeTracker.sln
git commit -m "feat(hooks-v2): add HookBridge generic relay console app"
```

---

## Task 3: IPC Service + Handler Interfaces

**Files:**
- Create: `src/ClaudeTracker/Services/Interfaces/IHookIpcService.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/IHookEventHandler.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/IHookEventObserver.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/IHookEventDispatcher.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/IActivityService.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/ISessionTrackingService.cs`
- Create: `src/ClaudeTracker/Services/HookIpcService.cs`

**Step 1: Create IHookIpcService.cs**

Create `src/ClaudeTracker/Services/Interfaces/IHookIpcService.cs`:

```csharp
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IHookIpcService : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();

    /// <summary>Fired for every hook event received from HookBridge.</summary>
    event Func<HookEvent, Task<HookResponse>>? EventReceived;
}
```

**Step 2: Create IHookEventHandler.cs**

Create `src/ClaudeTracker/Services/Interfaces/IHookEventHandler.cs`:

```csharp
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Handles interactive hook events that require a response back to Claude Code.</summary>
public interface IHookEventHandler
{
    bool CanHandle(string eventName);
    Task<HookResponse> HandleAsync(HookEvent evt);
}
```

**Step 3: Create IHookEventObserver.cs**

Create `src/ClaudeTracker/Services/Interfaces/IHookEventObserver.cs`:

```csharp
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Observes all hook events without blocking. Used for activity recording and session tracking.</summary>
public interface IHookEventObserver
{
    void Observe(HookEvent evt);
}
```

**Step 4: Create IHookEventDispatcher.cs**

Create `src/ClaudeTracker/Services/Interfaces/IHookEventDispatcher.cs`:

```csharp
namespace ClaudeTracker.Services.Interfaces;

public interface IHookEventDispatcher
{
    void Initialize();
}
```

**Step 5: Create IActivityService.cs**

Create `src/ClaudeTracker/Services/Interfaces/IActivityService.cs`:

```csharp
using System.Collections.ObjectModel;
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IActivityService
{
    ObservableCollection<ActivityEntry> RecentFeed { get; }
    void Record(ActivityEntry entry);
    void Clear();
}
```

**Step 6: Create ISessionTrackingService.cs**

Create `src/ClaudeTracker/Services/Interfaces/ISessionTrackingService.cs`:

```csharp
using System.Collections.ObjectModel;
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface ISessionTrackingService
{
    ObservableCollection<SessionState> ActiveSessions { get; }
    int ActiveSessionCount { get; }
    event EventHandler? SessionsChanged;

    void RegisterSession(string sessionId, string projectDirectory, string permissionMode, string? model);
    void EndSession(string sessionId);
    void RecordActivity(string sessionId, ActivityEntry entry);
    void RegisterSubagent(string sessionId, string agentId, string? agentType);
    void EndSubagent(string sessionId, string agentId);
    void PruneStale();
}
```

**Step 7: Create HookIpcService.cs**

Create `src/ClaudeTracker/Services/HookIpcService.cs`:

```csharp
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using Microsoft.Win32.SafeHandles;

namespace ClaudeTracker.Services;

public class HookIpcService : IHookIpcService
{
    private CancellationTokenSource? _cts;
    private readonly List<Task> _listenerTasks = new();

    public bool IsRunning { get; private set; }
    public event Func<HookEvent, Task<HookResponse>>? EventReceived;

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        IsRunning = true;

        for (int i = 0; i < Constants.Hooks.MaxConcurrentConnections; i++)
            _listenerTasks.Add(Task.Run(() => ListenLoop(_cts.Token)));

        LoggingService.Instance.Log($"Hook IPC server started on pipe: {Constants.Hooks.PipeName}");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        IsRunning = false;
        Task.WhenAll(_listenerTasks).ContinueWith(_ => _listenerTasks.Clear());
        LoggingService.Instance.Log("Hook IPC server stopped");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    Constants.Hooks.PipeName,
                    PipeDirection.InOut,
                    Constants.Hooks.MaxConcurrentConnections,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                await HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError("Hook IPC listener error", ex);
            }
            finally
            {
                if (pipe != null)
                {
                    try { pipe.Disconnect(); } catch { }
                    await pipe.DisposeAsync();
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var evt = await ReadEventAsync(pipe, ct);
        if (evt == null) return;

        HookResponse response;
        if (EventReceived != null)
        {
            try
            {
                // Monitor pipe disconnect for interactive events
                using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var responseTcs = new TaskCompletionSource<HookResponse>();
                var monitorTask = Constants.Hooks.InteractiveEvents.Contains(evt.EventName)
                    ? MonitorPipeDisconnectAsync(pipe, responseTcs, monitorCts.Token)
                    : Task.CompletedTask;

                var handlerTask = EventReceived.Invoke(evt);
                var completed = await Task.WhenAny(handlerTask, responseTcs.Task);

                monitorCts.Cancel();
                try { await monitorTask; } catch { }

                if (completed == responseTcs.Task)
                {
                    // Pipe disconnected — client answered in terminal
                    response = new HookResponse { RequestId = evt.RequestId, Success = true };
                }
                else
                {
                    response = await handlerTask;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError($"Error handling hook event {evt.EventName}", ex);
                response = new HookResponse { RequestId = evt.RequestId, Success = false };
            }
        }
        else
        {
            response = new HookResponse { RequestId = evt.RequestId, Success = true };
        }

        try
        {
            if (pipe.IsConnected)
                await WriteResponseAsync(pipe, response, ct);
        }
        catch (IOException) { /* client already disconnected */ }
    }

    private async Task<HookEvent?> ReadEventAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        if (await ReadExactAsync(pipe, lengthBuf, ct) < 4) return null;

        var length = BitConverter.ToInt32(lengthBuf, 0);
        if (length <= 0 || length > Constants.Hooks.MaxMessageSize) return null;

        var payloadBuf = new byte[length];
        if (await ReadExactAsync(pipe, payloadBuf, ct) < length) return null;

        return JsonSerializer.Deserialize<HookEvent>(Encoding.UTF8.GetString(payloadBuf));
    }

    private static async Task WriteResponseAsync(NamedPipeServerStream pipe, HookResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(response);
        var payload = Encoding.UTF8.GetBytes(json);
        await pipe.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
        await pipe.WriteAsync(payload, ct);
        await pipe.FlushAsync(ct);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(
        SafePipeHandle hNamedPipe, IntPtr lpBuffer, uint nBufferSize,
        IntPtr lpBytesRead, out uint lpTotalBytesAvail, IntPtr lpBytesLeftThisMessage);

    private static async Task MonitorPipeDisconnectAsync(
        NamedPipeServerStream pipe,
        TaskCompletionSource<HookResponse> tcs,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !tcs.Task.IsCompleted)
            {
                await Task.Delay(500, ct);
                if (!PeekNamedPipe(pipe.SafePipeHandle, IntPtr.Zero, 0,
                        IntPtr.Zero, out _, IntPtr.Zero))
                    break;
            }
            tcs.TrySetResult(new HookResponse { Success = true });
        }
        catch (OperationCanceledException) { }
        catch { tcs.TrySetResult(new HookResponse { Success = true }); }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
```

**Step 8: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 9: Commit**

```bash
git add src/ClaudeTracker/Services/Interfaces/IHookIpcService.cs src/ClaudeTracker/Services/Interfaces/IHookEventHandler.cs src/ClaudeTracker/Services/Interfaces/IHookEventObserver.cs src/ClaudeTracker/Services/Interfaces/IHookEventDispatcher.cs src/ClaudeTracker/Services/Interfaces/IActivityService.cs src/ClaudeTracker/Services/Interfaces/ISessionTrackingService.cs src/ClaudeTracker/Services/HookIpcService.cs
git commit -m "feat(hooks-v2): add IPC service, handler interfaces, and observer interfaces"
```

---

## Task 4: Event Dispatcher + Observers

**Files:**
- Create: `src/ClaudeTracker/Services/HookEventDispatcher.cs`
- Create: `src/ClaudeTracker/Services/ActivityService.cs`
- Create: `src/ClaudeTracker/Services/SessionTrackingService.cs`
- Create: `src/ClaudeTracker/Services/Observers/ActivityRecorder.cs`
- Create: `src/ClaudeTracker/Services/Observers/SessionTracker.cs`

**Step 1: Create HookEventDispatcher.cs**

Create `src/ClaudeTracker/Services/HookEventDispatcher.cs`:

```csharp
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

/// <summary>
/// Routes incoming HookEvents to the appropriate handler (interactive) and
/// broadcasts all events to observers (activity recording, session tracking).
/// </summary>
public class HookEventDispatcher : IHookEventDispatcher
{
    private readonly IHookIpcService _ipcService;
    private readonly IEnumerable<IHookEventHandler> _handlers;
    private readonly IEnumerable<IHookEventObserver> _observers;

    public HookEventDispatcher(
        IHookIpcService ipcService,
        IEnumerable<IHookEventHandler> handlers,
        IEnumerable<IHookEventObserver> observers)
    {
        _ipcService = ipcService;
        _handlers = handlers;
        _observers = observers;
    }

    public void Initialize()
    {
        _ipcService.EventReceived += OnEventReceived;
    }

    private async Task<HookResponse> OnEventReceived(HookEvent evt)
    {
        // Broadcast to all observers (fire-and-forget, never blocks)
        foreach (var observer in _observers)
        {
            try { observer.Observe(evt); }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError($"Observer error for {evt.EventName}", ex);
            }
        }

        // Route to interactive handler if one matches
        if (Constants.Hooks.InteractiveEvents.Contains(evt.EventName))
        {
            foreach (var handler in _handlers)
            {
                if (handler.CanHandle(evt.EventName))
                {
                    try
                    {
                        return await handler.HandleAsync(evt);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.LogError($"Handler error for {evt.EventName}", ex);
                    }
                }
            }
        }

        // Default: acknowledge without output
        return new HookResponse { RequestId = evt.RequestId, Success = true };
    }
}
```

**Step 2: Create ActivityService.cs**

Create `src/ClaudeTracker/Services/ActivityService.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ActivityService : IActivityService
{
    private readonly ISettingsService _settingsService;

    public ObservableCollection<ActivityEntry> RecentFeed { get; } = new();

    public ActivityService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Record(ActivityEntry entry)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RecentFeed.Insert(0, entry);

            var max = _settingsService.Settings.HookMaxFeedEntries;
            while (RecentFeed.Count > max)
                RecentFeed.RemoveAt(RecentFeed.Count - 1);
        });
    }

    public void Clear()
    {
        Application.Current?.Dispatcher.Invoke(() => RecentFeed.Clear());
    }
}
```

**Step 3: Create SessionTrackingService.cs**

Create `src/ClaudeTracker/Services/SessionTrackingService.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class SessionTrackingService : ISessionTrackingService
{
    private readonly object _lock = new();

    public ObservableCollection<SessionState> ActiveSessions { get; } = new();
    public int ActiveSessionCount => ActiveSessions.Count;
    public event EventHandler? SessionsChanged;

    public void RegisterSession(string sessionId, string projectDirectory, string permissionMode, string? model)
    {
        lock (_lock)
        {
            // Update existing session (e.g. resume) or add new
            var existing = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (existing != null)
            {
                existing.IsActive = true;
                existing.LastActivityAt = DateTime.UtcNow;
                existing.PermissionMode = permissionMode;
                if (model != null) existing.Model = model;
                return;
            }
        }

        var session = new SessionState
        {
            SessionId = sessionId,
            ProjectDirectory = projectDirectory,
            PermissionMode = permissionMode,
            Model = model
        };

        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock) { ActiveSessions.Add(session); }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void EndSession(string sessionId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    ActiveSessions.Remove(session);
                    SessionsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        });
    }

    public void RecordActivity(string sessionId, ActivityEntry entry)
    {
        lock (_lock)
        {
            var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null) return;

            session.LastActivityAt = DateTime.UtcNow;
            session.CurrentActivity = entry.Summary;

            if (entry.ToolName != null)
                session.ToolCallCount++;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                session.Activities.Insert(0, entry);
                while (session.Activities.Count > Constants.Hooks.DefaultMaxActivityEntries)
                    session.Activities.RemoveAt(session.Activities.Count - 1);
            });

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RegisterSubagent(string sessionId, string agentId, string? agentType)
    {
        lock (_lock)
        {
            var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null) return;

            if (!session.ActiveSubagents.Contains(agentId))
            {
                session.ActiveSubagents.Add(agentId);
                session.SubagentCount++;
                session.LastActivityAt = DateTime.UtcNow;
                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void EndSubagent(string sessionId, string agentId)
    {
        lock (_lock)
        {
            var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
            session?.ActiveSubagents.Remove(agentId);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PruneStale()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Constants.Hooks.StaleSessionMinutes);

        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var stale = ActiveSessions.Where(s => s.LastActivityAt < cutoff).ToList();
                foreach (var s in stale)
                    ActiveSessions.Remove(s);
                if (stale.Count > 0)
                    SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }
}
```

**Step 4: Create ActivityRecorder.cs**

Create `src/ClaudeTracker/Services/Observers/ActivityRecorder.cs`:

```csharp
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Observers;

/// <summary>Observes all hook events and records them as ActivityEntry items.</summary>
public class ActivityRecorder : IHookEventObserver
{
    private readonly IActivityService _activityService;
    private readonly ISessionTrackingService _sessionTracking;

    public ActivityRecorder(IActivityService activityService, ISessionTrackingService sessionTracking)
    {
        _activityService = activityService;
        _sessionTracking = sessionTracking;
    }

    public void Observe(HookEvent evt)
    {
        var json = TryParse(evt.Payload);
        var sessionId = json?["session_id"]?.GetValue<string>() ?? "";

        var entry = new ActivityEntry
        {
            SessionId = sessionId,
            EventName = evt.EventName,
            Timestamp = evt.Timestamp,
            Summary = BuildSummary(evt.EventName, json),
            Icon = GetIcon(evt.EventName),
            ToolName = GetToolName(evt.EventName, json),
            RawPayload = evt.Payload
        };

        _activityService.Record(entry);

        if (!string.IsNullOrEmpty(sessionId))
            _sessionTracking.RecordActivity(sessionId, entry);
    }

    private static string BuildSummary(string eventName, JsonNode? json) => eventName switch
    {
        "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionRequest" =>
            FormatToolSummary(json?["tool_name"]?.GetValue<string>() ?? "?", json),
        "Notification" =>
            json?["message"]?.GetValue<string>() ?? "Notification",
        "Stop" => "Task completed",
        "SessionStart" => $"Session started ({json?["source"]?.GetValue<string>() ?? "?"})",
        "SessionEnd" => $"Session ended ({json?["reason"]?.GetValue<string>() ?? "?"})",
        "SubagentStart" => $"Subagent: {json?["agent_type"]?.GetValue<string>() ?? "?"}",
        "SubagentStop" => $"Subagent finished: {json?["agent_type"]?.GetValue<string>() ?? "?"}",
        "UserPromptSubmit" => "User prompt submitted",
        "PreCompact" or "PostCompact" => $"Context {(eventName == "PreCompact" ? "compacting" : "compacted")}",
        "WorktreeCreate" => $"Worktree: {json?["name"]?.GetValue<string>() ?? "?"}",
        "WorktreeRemove" => "Worktree removed",
        "InstructionsLoaded" => $"Loaded: {System.IO.Path.GetFileName(json?["file_path"]?.GetValue<string>() ?? "?")}",
        "ConfigChange" => $"Config: {json?["source"]?.GetValue<string>() ?? "?"}",
        "Elicitation" => $"MCP: {json?["mcp_server_name"]?.GetValue<string>() ?? "?"} requesting input",
        "ElicitationResult" => $"MCP: user responded",
        "TeammateIdle" => $"Teammate idle: {json?["teammate_name"]?.GetValue<string>() ?? "?"}",
        "TaskCompleted" => $"Task done: {json?["task_subject"]?.GetValue<string>() ?? "?"}",
        _ => eventName
    };

    private static string FormatToolSummary(string toolName, JsonNode? json) => toolName switch
    {
        "Bash" => $"Bash: {Truncate(json?["tool_input"]?["command"]?.GetValue<string>(), 60)}",
        "Edit" => $"Edit {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Write" => $"Write {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Read" => $"Read {Truncate(json?["tool_input"]?["file_path"]?.GetValue<string>(), 50)}",
        "Grep" => $"Grep: {Truncate(json?["tool_input"]?["pattern"]?.GetValue<string>(), 40)}",
        "Glob" => $"Glob: {Truncate(json?["tool_input"]?["pattern"]?.GetValue<string>(), 40)}",
        "WebFetch" => $"Fetch: {Truncate(json?["tool_input"]?["url"]?.GetValue<string>(), 50)}",
        "WebSearch" => $"Search: {Truncate(json?["tool_input"]?["query"]?.GetValue<string>(), 50)}",
        "AskUserQuestion" => "Asking user question",
        _ => toolName.StartsWith("mcp__") ? $"MCP: {toolName}" : toolName
    };

    private static ActivityIcon GetIcon(string eventName) => eventName switch
    {
        "PreToolUse" or "PostToolUse" or "PostToolUseFailure" => ActivityIcon.Tool,
        "PermissionRequest" => ActivityIcon.Permission,
        "SessionStart" or "SessionEnd" => ActivityIcon.Session,
        "SubagentStart" or "SubagentStop" => ActivityIcon.Subagent,
        "Notification" => ActivityIcon.Notification,
        _ => ActivityIcon.System
    };

    private static string? GetToolName(string eventName, JsonNode? json)
    {
        if (eventName is "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionRequest")
            return json?["tool_name"]?.GetValue<string>();
        return null;
    }

    private static string Truncate(string? s, int max) =>
        s == null ? "?" : s.Length <= max ? s : s[..max] + "...";

    private static JsonNode? TryParse(string payload)
    {
        try { return JsonNode.Parse(payload); } catch { return null; }
    }
}
```

**Step 5: Create SessionTracker.cs**

Create `src/ClaudeTracker/Services/Observers/SessionTracker.cs`:

```csharp
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Observers;

/// <summary>Observes session lifecycle events and manages SessionState objects.</summary>
public class SessionTracker : IHookEventObserver
{
    private readonly ISessionTrackingService _sessionTracking;

    public SessionTracker(ISessionTrackingService sessionTracking)
    {
        _sessionTracking = sessionTracking;
    }

    public void Observe(HookEvent evt)
    {
        var json = TryParse(evt.Payload);
        if (json == null) return;

        var sessionId = json["session_id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(sessionId)) return;

        switch (evt.EventName)
        {
            case "SessionStart":
                _sessionTracking.RegisterSession(
                    sessionId,
                    json["cwd"]?.GetValue<string>() ?? "",
                    json["permission_mode"]?.GetValue<string>() ?? "",
                    json["model"]?.GetValue<string>());
                break;

            case "SessionEnd":
                _sessionTracking.EndSession(sessionId);
                break;

            case "SubagentStart":
                var agentId = json["agent_id"]?.GetValue<string>() ?? "";
                var agentType = json["agent_type"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(agentId))
                    _sessionTracking.RegisterSubagent(sessionId, agentId, agentType);
                break;

            case "SubagentStop":
                var endAgentId = json["agent_id"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(endAgentId))
                    _sessionTracking.EndSubagent(sessionId, endAgentId);
                break;

            case "Stop":
                // Stop doesn't end the session but marks last activity
                break;
        }
    }

    private static JsonNode? TryParse(string payload)
    {
        try { return JsonNode.Parse(payload); } catch { return null; }
    }
}
```

**Step 6: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add src/ClaudeTracker/Services/HookEventDispatcher.cs src/ClaudeTracker/Services/ActivityService.cs src/ClaudeTracker/Services/SessionTrackingService.cs src/ClaudeTracker/Services/Observers/
git commit -m "feat(hooks-v2): add event dispatcher, activity service, and session tracking"
```

---

## Task 5: Interactive Handlers (7 handlers)

**Files:**
- Create: `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/PreToolUseHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/ElicitationHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/UserPromptHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/StopHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/SubagentStopHandler.cs`
- Create: `src/ClaudeTracker/Services/Handlers/ConfigChangeHandler.cs`

**Step 1: Create PermissionRequestHandler.cs**

Create `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs`:

This is the most complex handler — parses PermissionRequest JSON, shows UI popup (via event), builds response JSON including AskUserQuestion `updatedInput` and AlwaysAllow `updatedPermissions`.

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

public class PermissionRequestHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;

    /// <summary>Raised on UI thread to show permission popup.</summary>
    public event EventHandler<HookInteractiveEventArgs<PermissionRequestInfo>>? PermissionRequested;

    public PermissionRequestHandler(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool CanHandle(string eventName) => eventName == "PermissionRequest";

    public async Task<HookResponse> HandleAsync(HookEvent evt)
    {
        if (!_settingsService.Settings.HookPermissionPopupsEnabled)
            return new HookResponse { RequestId = evt.RequestId, Success = true };

        var json = JsonNode.Parse(evt.Payload);
        if (json == null)
            return new HookResponse { RequestId = evt.RequestId, Success = false };

        var info = ParsePermissionRequest(evt.RequestId, json);
        var tcs = new TaskCompletionSource<HookResponse>();

        PermissionRequested?.Invoke(this, new HookInteractiveEventArgs<PermissionRequestInfo>
        {
            Info = info,
            ResponseSource = tcs
        });

        return await tcs.Task;
    }

    private static PermissionRequestInfo ParsePermissionRequest(string requestId, JsonNode json)
    {
        var toolInput = new Dictionary<string, object?>();
        if (json["tool_input"] is JsonObject toolInputObj)
        {
            foreach (var prop in toolInputObj)
                toolInput[prop.Key] = prop.Value?.ToString();
        }

        var info = new PermissionRequestInfo
        {
            RequestId = requestId,
            ToolName = json["tool_name"]?.GetValue<string>() ?? "",
            ToolInput = toolInput,
            SessionId = json["session_id"]?.GetValue<string>() ?? "",
            Cwd = json["cwd"]?.GetValue<string>() ?? "",
            PermissionMode = json["permission_mode"]?.GetValue<string>() ?? ""
        };

        if (json["permission_suggestions"] is JsonArray suggestionsArr)
        {
            foreach (var s in suggestionsArr)
            {
                var suggestion = new PermissionSuggestion
                {
                    Type = s?["type"]?.GetValue<string>() ?? "",
                    Behavior = s?["behavior"]?.GetValue<string>() ?? "",
                    Destination = s?["destination"]?.GetValue<string>() ?? "",
                    Tool = s?["tool"]?.GetValue<string>() ?? "",
                    Prefix = s?["prefix"]?.GetValue<string>() ?? ""
                };

                if (s?["rules"] is JsonArray rulesArr)
                {
                    foreach (var r in rulesArr)
                        suggestion.Rules.Add(new PermissionRule
                        {
                            ToolName = r?["toolName"]?.GetValue<string>() ?? "",
                            RuleContent = r?["ruleContent"]?.GetValue<string>() ?? ""
                        });
                }

                if (s?["directories"] is JsonArray dirsArr)
                {
                    foreach (var d in dirsArr)
                    {
                        var dir = d?.GetValue<string>();
                        if (!string.IsNullOrEmpty(dir))
                            suggestion.Directories.Add(dir);
                    }
                }

                info.PermissionSuggestions.Add(suggestion);
            }
        }

        return info;
    }

    /// <summary>Build Claude Code response JSON from a PermissionDecisionResult.</summary>
    public static string BuildResponseJson(PermissionDecisionResult result)
    {
        if (result.Decision == PermissionDecision.HandleInTerminal)
            return ""; // null jsonOutput → fallback to terminal

        if (result.Decision == PermissionDecision.AlwaysAllow && result.AppliedSuggestion != null)
        {
            var suggestion = result.AppliedSuggestion;
            var permObj = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(suggestion.Type)) permObj["type"] = suggestion.Type;
            if (!string.IsNullOrEmpty(suggestion.Behavior)) permObj["behavior"] = suggestion.Behavior;
            if (!string.IsNullOrEmpty(suggestion.Destination)) permObj["destination"] = suggestion.Destination;
            if (suggestion.Rules.Count > 0)
                permObj["rules"] = suggestion.Rules.Select(r => new { toolName = r.ToolName, ruleContent = r.RuleContent }).ToArray();
            if (suggestion.Directories.Count > 0)
                permObj["directories"] = suggestion.Directories.ToArray();
            if (!string.IsNullOrEmpty(suggestion.Tool)) permObj["tool"] = suggestion.Tool;
            if (!string.IsNullOrEmpty(suggestion.Prefix)) permObj["prefix"] = suggestion.Prefix;

            var decision = new Dictionary<string, object>
            {
                ["behavior"] = "allow",
                ["updatedPermissions"] = new[] { permObj }
            };

            if (result.UpdatedInput != null)
                decision["updatedInput"] = result.UpdatedInput;

            return JsonSerializer.Serialize(new
            {
                hookSpecificOutput = new { hookEventName = "PermissionRequest", decision }
            });
        }
        else
        {
            var behavior = result.Decision == PermissionDecision.Allow ? "allow" : "deny";
            var decision = new Dictionary<string, object> { ["behavior"] = behavior };

            if (result.UpdatedInput != null)
                decision["updatedInput"] = result.UpdatedInput;

            return JsonSerializer.Serialize(new
            {
                hookSpecificOutput = new { hookEventName = "PermissionRequest", decision }
            });
        }
    }
}
```

**Step 2: Create remaining handlers**

Each remaining handler follows the same pattern but is simpler — most just passthrough (return success) for now, with the infrastructure to add UI interaction later.

Create `src/ClaudeTracker/Services/Handlers/PreToolUseHandler.cs`:

```csharp
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>PreToolUse handler — passthrough for now, can add interception logic later.</summary>
public class PreToolUseHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == "PreToolUse";

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        // Passthrough — let Claude proceed. Extend later for auto-allow rules.
        return Task.FromResult(new HookResponse { RequestId = evt.RequestId, Success = true });
    }
}
```

Create `src/ClaudeTracker/Services/Handlers/ElicitationHandler.cs`:

```csharp
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>Elicitation handler — shows MCP form popup for user input.</summary>
public class ElicitationHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;

    public event EventHandler<HookInteractiveEventArgs<ElicitationInfo>>? ElicitationRequested;

    public ElicitationHandler(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool CanHandle(string eventName) => eventName == "Elicitation";

    public async Task<HookResponse> HandleAsync(HookEvent evt)
    {
        if (!_settingsService.Settings.HookElicitationPopupsEnabled)
            return new HookResponse { RequestId = evt.RequestId, Success = true };

        var json = JsonNode.Parse(evt.Payload);
        if (json == null)
            return new HookResponse { RequestId = evt.RequestId, Success = false };

        var info = new ElicitationInfo
        {
            McpServerName = json["mcp_server_name"]?.GetValue<string>() ?? "",
            Message = json["message"]?.GetValue<string>() ?? "",
            Mode = json["mode"]?.GetValue<string>(),
            Url = json["url"]?.GetValue<string>(),
            ElicitationId = json["elicitation_id"]?.GetValue<string>() ?? "",
            RequestedSchema = json["requested_schema"]?.ToJsonString(),
            SessionId = json["session_id"]?.GetValue<string>() ?? "",
            Cwd = json["cwd"]?.GetValue<string>() ?? ""
        };

        var tcs = new TaskCompletionSource<HookResponse>();

        ElicitationRequested?.Invoke(this, new HookInteractiveEventArgs<ElicitationInfo>
        {
            Info = info,
            ResponseSource = tcs
        });

        return await tcs.Task;
    }
}
```

Create `src/ClaudeTracker/Services/Handlers/UserPromptHandler.cs`:

```csharp
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>UserPromptSubmit handler — passthrough for now.</summary>
public class UserPromptHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == "UserPromptSubmit";

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        return Task.FromResult(new HookResponse { RequestId = evt.RequestId, Success = true });
    }
}
```

Create `src/ClaudeTracker/Services/Handlers/StopHandler.cs`:

```csharp
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>Stop handler — sends notification when Claude finishes.</summary>
public class StopHandler : IHookEventHandler
{
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;

    public StopHandler(ISettingsService settingsService, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
    }

    public bool CanHandle(string eventName) => eventName == "Stop";

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        var prefs = _settingsService.Settings.HookNotificationPreferences;
        if (prefs.TryGetValue("stop", out var enabled) && enabled)
        {
            var json = JsonNode.Parse(evt.Payload);
            var cwd = json?["cwd"]?.GetValue<string>() ?? "";
            var projectName = cwd.Length > 0 ? System.IO.Path.GetFileName(cwd) : "";
            var prefix = projectName.Length > 0 ? $"[{projectName}] " : "";

            ((NotificationService)_notificationService).SendNotification(
                $"{prefix}Task Complete", "Claude Code has finished.",
                Views.NotificationPopup.NotificationLevel.Info);
        }

        return Task.FromResult(new HookResponse { RequestId = evt.RequestId, Success = true });
    }
}
```

Create `src/ClaudeTracker/Services/Handlers/SubagentStopHandler.cs`:

```csharp
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>SubagentStop handler — passthrough for now.</summary>
public class SubagentStopHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == "SubagentStop";

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        return Task.FromResult(new HookResponse { RequestId = evt.RequestId, Success = true });
    }
}
```

Create `src/ClaudeTracker/Services/Handlers/ConfigChangeHandler.cs`:

```csharp
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services.Handlers;

/// <summary>ConfigChange handler — passthrough for now.</summary>
public class ConfigChangeHandler : IHookEventHandler
{
    public bool CanHandle(string eventName) => eventName == "ConfigChange";

    public Task<HookResponse> HandleAsync(HookEvent evt)
    {
        return Task.FromResult(new HookResponse { RequestId = evt.RequestId, Success = true });
    }
}
```

**Step 3: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/ClaudeTracker/Services/Handlers/
git commit -m "feat(hooks-v2): add 7 interactive event handlers"
```

---

## Task 6: DI Registration + Startup Wiring

**Files:**
- Modify: `src/ClaudeTracker/App.xaml.cs` (ConfigureServices + OnStartup)

**Step 1: Add DI registrations to ConfigureServices**

In `src/ClaudeTracker/App.xaml.cs`, add to the end of `ConfigureServices()` method (before the closing brace):

```csharp
            // ── Hooks Integration ──
            services.AddSingleton<IHookIpcService, HookIpcService>();
            services.AddSingleton<IHookEventDispatcher, HookEventDispatcher>();

            // Interactive handlers
            var permHandler = new Handlers.PermissionRequestHandler(/* resolved later */);
            services.AddSingleton<IHookEventHandler, Handlers.PermissionRequestHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.PreToolUseHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.ElicitationHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.UserPromptHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.StopHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.SubagentStopHandler>();
            services.AddSingleton<IHookEventHandler, Handlers.ConfigChangeHandler>();

            // Observers
            services.AddSingleton<IHookEventObserver, Observers.ActivityRecorder>();
            services.AddSingleton<IHookEventObserver, Observers.SessionTracker>();

            // Services
            services.AddSingleton<IActivityService, ActivityService>();
            services.AddSingleton<ISessionTrackingService, SessionTrackingService>();
```

Add required `using` statements to the top of `App.xaml.cs`:

```csharp
using ClaudeTracker.Services.Handlers;
using ClaudeTracker.Services.Observers;
```

**Step 2: Add hooks startup in OnStartup**

In `src/ClaudeTracker/App.xaml.cs`, add after network monitor startup (after `networkMonitor.Start();`):

```csharp
        // Start hooks IPC server
        if (settingsService.Settings.HooksEnabled)
        {
            var hookIpcService = _services.GetRequiredService<IHookIpcService>();
            var hookDispatcher = _services.GetRequiredService<IHookEventDispatcher>();
            hookDispatcher.Initialize();
            hookIpcService.Start();

            // Wire PermissionRequestHandler to show popup UI
            var permHandler = _services.GetServices<IHookEventHandler>()
                .OfType<PermissionRequestHandler>().FirstOrDefault();
            if (permHandler != null)
            {
                permHandler.PermissionRequested += (_, args) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var popup = new Views.PermissionRequestPopup(args.Info, args.ResponseSource);
                            popup.Show();
                            popup.Activate();
                        }
                        catch (Exception ex)
                        {
                            LoggingService.Instance.LogError("Failed to show permission popup", ex);
                            args.ResponseSource.TrySetResult(new Models.HookResponse
                            {
                                RequestId = args.Info.RequestId,
                                Success = true
                            });
                        }
                    });
                };
            }

            // Start stale session pruning timer
            var sessionTracking = _services.GetRequiredService<ISessionTrackingService>();
            var pruneTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            pruneTimer.Tick += (_, _) => sessionTracking.PruneStale();
            pruneTimer.Start();

            LoggingService.Instance.Log("Hooks integration initialized");
        }
```

**Step 3: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/ClaudeTracker/App.xaml.cs
git commit -m "feat(hooks-v2): wire DI registration and startup for hooks integration"
```

---

## Task 7: Permission Request Popup UI

**Files:**
- Create: `src/ClaudeTracker/Views/PermissionRequestPopup.xaml`
- Create: `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs`
- Create: `src/ClaudeTracker/Utilities/PopupStackManager.cs`

**Step 1: Create PopupStackManager.cs**

Copy the pattern from reference commit `6a5d523` — tracks open popup windows and positions them in a vertical stack at bottom-right of screen.

Create `src/ClaudeTracker/Utilities/PopupStackManager.cs` with the same implementation from the reference (stacking logic, repositioning, cleanup on close).

**Step 2: Create PermissionRequestPopup.xaml**

Create `src/ClaudeTracker/Views/PermissionRequestPopup.xaml` — dark theme popup with:
- Header: lock icon + "Permission Request" + project path
- Tool info panel: tool name (purple) + formatted input (Consolas)
- Diff panel (for Edit tool): side-by-side old/new with sync scroll
- Ask panel (for AskUserQuestion): questions with selectable options + "Other" free-text
- Write panel (for Write tool): file content preview
- Queue badge
- Buttons: Allow | Deny, Always Allow (dynamic from suggestions) | Terminal

Use the XAML from reference commit as the base template.

**Step 3: Create PermissionRequestPopup.xaml.cs**

Create `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs` — handles:
- Parsing tool input for preview (Edit diff, Write content, AskUserQuestion questions)
- Option selection tracking with multi-select support
- Building PermissionDecisionResult with UpdatedInput for AskUserQuestion
- Auto-close when decision made externally (pipe disconnect)
- Slide-in animation + popup stacking

The code-behind should use the same `TaskCompletionSource<HookResponse>` pattern but build the response using `PermissionRequestHandler.BuildResponseJson()`.

**Note:** This is the same UI as the reference commit. Adapt to use the new `HookResponse` type instead of `PermissionDecisionResult` as the TCS type. The popup should:
1. Receive `HookInteractiveEventArgs<PermissionRequestInfo>`
2. Let user choose (Allow/Deny/AlwaysAllow/Terminal)
3. Build JSON response via `PermissionRequestHandler.BuildResponseJson()`
4. Set `ResponseSource.TrySetResult(new HookResponse { ..., JsonOutput = json })`

**Step 4: Build and test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Views/PermissionRequestPopup.xaml src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs src/ClaudeTracker/Utilities/PopupStackManager.cs
git commit -m "feat(hooks-v2): add permission request popup with diff/ask/write previews"
```

---

## Task 8: Popover UI — Sessions Card + Activity Feed

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml.cs`

**Step 1: Add hooks properties to PopoverViewModel.cs**

Add these properties after the existing `ObservableProperty` declarations (around line 64):

```csharp
    [ObservableProperty] private int _activeSessionCount;
    [ObservableProperty] private bool _hasActiveSessions;
    [ObservableProperty] private bool _showActivityFeed;
```

Add `ObservableCollection` properties:

```csharp
    public ObservableCollection<SessionState> ActiveSessions { get; } = new();
    public ObservableCollection<ActivityEntry> ActivityFeed { get; } = new();
```

Update the constructor to accept `ISessionTrackingService` and `IActivityService`:

```csharp
    public PopoverViewModel(
        IProfileService profileService,
        IUsageRefreshCoordinator refreshCoordinator,
        ISettingsService settingsService,
        ISessionTrackingService sessionTracking,
        IActivityService activityService)
```

Wire session tracking changes:

```csharp
        sessionTracking.SessionsChanged += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ActiveSessions.Clear();
                foreach (var s in sessionTracking.ActiveSessions)
                    ActiveSessions.Add(s);
                ActiveSessionCount = sessionTracking.ActiveSessionCount;
                HasActiveSessions = ActiveSessionCount > 0;
            });
        };

        ShowActivityFeed = settingsService.Settings.HookActivityFeedEnabled;
```

**Step 2: Add sessions card + activity feed to PopoverWindow.xaml**

Add before the `LastUpdatedText` element (around line 161), after the API Credits card:

```xml
                    <!-- ── Active Claude Code Sessions ── -->
                    <Border x:Name="SessionsCard" Style="{StaticResource UsageCardStyle}" Visibility="Collapsed">
                        <StackPanel>
                            <Grid Margin="0,0,0,6">
                                <StackPanel Orientation="Horizontal">
                                    <materialDesign:PackIcon Kind="Console" Width="15" Height="15"
                                                             Foreground="#888" VerticalAlignment="Center"
                                                             Margin="0,0,5,0" />
                                    <TextBlock Text="Active Sessions" FontSize="13" FontWeight="Medium" Foreground="#666"
                                               VerticalAlignment="Center" />
                                </StackPanel>
                                <TextBlock x:Name="SessionCountText" HorizontalAlignment="Right"
                                           FontSize="13" FontWeight="SemiBold" Foreground="#444"
                                           VerticalAlignment="Center" />
                            </Grid>
                            <ItemsControl x:Name="SessionsList">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border Background="#08000000" CornerRadius="4" Padding="8,5" Margin="0,2"
                                                Cursor="Hand">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <StackPanel>
                                                    <TextBlock Text="{Binding ProjectName}" FontSize="12"
                                                               FontWeight="Medium" Foreground="#555"
                                                               TextTrimming="CharacterEllipsis" />
                                                    <TextBlock Text="{Binding CurrentActivity}" FontSize="10.5"
                                                               Foreground="#999" TextTrimming="CharacterEllipsis"
                                                               Margin="0,1,0,0" />
                                                </StackPanel>
                                                <TextBlock Grid.Column="1" Text="{Binding ToolCallCount, StringFormat='{}{0}⚡'}"
                                                           FontSize="11" Foreground="#888"
                                                           VerticalAlignment="Center" Margin="0,0,6,0" />
                                                <Ellipse Grid.Column="2" Width="7" Height="7"
                                                         Fill="#4CAF50" VerticalAlignment="Center" />
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </Border>

                    <!-- ── Recent Activity Feed ── -->
                    <Border x:Name="ActivityFeedCard" Style="{StaticResource UsageCardStyle}" Visibility="Collapsed">
                        <StackPanel>
                            <TextBlock Text="Recent Activity" FontSize="13" FontWeight="Medium"
                                       Foreground="#666" Margin="0,0,0,6" />
                            <ItemsControl x:Name="ActivityFeedList">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,2">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <TextBlock x:Name="ActivityIcon" FontSize="11" Width="16"
                                                       Foreground="#888" VerticalAlignment="Center" />
                                            <TextBlock Grid.Column="1" Text="{Binding Summary}"
                                                       FontSize="11" Foreground="#666"
                                                       TextTrimming="CharacterEllipsis" Margin="2,0,6,0"
                                                       VerticalAlignment="Center" />
                                            <TextBlock Grid.Column="2" FontSize="10" Foreground="#AAA"
                                                       VerticalAlignment="Center" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </Border>
```

**Step 3: Update PopoverWindow.xaml.cs**

In the constructor, wire sessions and activity feed:

```csharp
        SessionsList.ItemsSource = _viewModel.ActiveSessions;
        ActivityFeedList.ItemsSource = _viewModel.ActivityFeed;
```

In `UpdateUI()`, add visibility logic:

```csharp
            // Sessions card
            SessionsCard.Visibility = _viewModel.HasActiveSessions ? Visibility.Visible : Visibility.Collapsed;
            if (_viewModel.HasActiveSessions)
                SessionCountText.Text = _viewModel.ActiveSessionCount.ToString();

            // Activity feed
            ActivityFeedCard.Visibility = _viewModel.ShowActivityFeed && _viewModel.ActivityFeed.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
```

**Step 4: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PopoverViewModel.cs src/ClaudeTracker/Views/PopoverWindow.xaml src/ClaudeTracker/Views/PopoverWindow.xaml.cs
git commit -m "feat(hooks-v2): add sessions card and activity feed to popover"
```

---

## Task 9: Hooks Settings View

**Files:**
- Create: `src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs`
- Create: `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml`
- Create: `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs`
- Modify: `src/ClaudeTracker/Views/SettingsWindow.xaml` (add nav button)
- Modify: `src/ClaudeTracker/Views/SettingsWindow.xaml.cs` (add nav handler)

**Step 1: Create HooksSettingsViewModel.cs**

Create `src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs`:

Follow the same pattern as `GeneralSettingsViewModel` — `[ObservableProperty]` for each setting, snapshot for HasUnsavedChanges, `[RelayCommand] Save()`.

Properties:
- HooksEnabled, PermissionPopupsEnabled, ElicitationPopupsEnabled
- ActivityFeedEnabled, MaxFeedEntries
- Notification preferences: NotifyStop, NotifyToolError, NotifyPermission, NotifyIdle, NotifyConfigChange, NotifySessionLifecycle, NotifySubagent

Save method: update `_settingsService.Settings` and call `_settingsService.Save()`. If `HooksEnabled` changed, call `Start()`/`Stop()` on IPC service.

**Step 2: Create HooksSettingsView.xaml**

Settings panel matching existing style. Sections:
- Enable toggle
- Permission/Elicitation popup toggles
- Notification checkboxes (7 categories)
- Activity feed toggle + max entries slider
- Install/Uninstall buttons (run HookBridge install/uninstall)
- Save button

**Step 3: Create HooksSettingsView.xaml.cs**

Wire controls to ViewModel following the same pattern as `GeneralSettingsView.xaml.cs` — event handlers that update ViewModel properties, Save button visibility bound to `HasUnsavedChanges`.

Install button: run `ClaudeTracker.HookBridge.exe install` via `Process.Start`.
Uninstall button: run `ClaudeTracker.HookBridge.exe uninstall` via `Process.Start`.

**Step 4: Add nav button to SettingsWindow.xaml**

Add a new RadioButton in the PREFERENCES section:

```xml
<RadioButton x:Name="NavHooks" Content="Hooks" Style="{StaticResource NavButton}" />
```

**Step 5: Wire nav in SettingsWindow.xaml.cs**

Add in constructor:

```csharp
NavHooks.Checked += (_, _) => ShowView<Settings.HooksSettingsView>();
```

Also register `HooksSettingsViewModel` as Transient in `App.xaml.cs ConfigureServices()`:

```csharp
services.AddTransient<HooksSettingsViewModel>();
```

**Step 6: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs src/ClaudeTracker/Views/SettingsWindow.xaml src/ClaudeTracker/Views/SettingsWindow.xaml.cs src/ClaudeTracker/App.xaml.cs
git commit -m "feat(hooks-v2): add hooks settings tab with notification preferences"
```

---

## Task 10: Notification Wiring for Hook Events

**Files:**
- Modify: `src/ClaudeTracker/App.xaml.cs` (wire notification events in OnStartup)

**Step 1: Wire notification events from observers**

In `App.xaml.cs OnStartup`, inside the `if (settingsService.Settings.HooksEnabled)` block, add notification wiring after the permission handler wiring:

```csharp
            // Wire hook notification observer for configurable notifications
            var notificationService = _services.GetRequiredService<INotificationService>();
            var activityService = _services.GetRequiredService<IActivityService>();

            activityService.RecentFeed.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (ActivityEntry entry in e.NewItems)
                {
                    var prefs = settingsService.Settings.HookNotificationPreferences;
                    var shouldNotify = entry.EventName switch
                    {
                        "PostToolUseFailure" => prefs.GetValueOrDefault("toolError", true),
                        "Notification" when entry.RawPayload.Contains("permission_prompt") =>
                            prefs.GetValueOrDefault("permission", true),
                        "Notification" when entry.RawPayload.Contains("idle_prompt") =>
                            prefs.GetValueOrDefault("idle", true),
                        "ConfigChange" => prefs.GetValueOrDefault("configChange", false),
                        "SessionStart" or "SessionEnd" => prefs.GetValueOrDefault("sessionLifecycle", false),
                        "SubagentStart" or "SubagentStop" => prefs.GetValueOrDefault("subagent", false),
                        _ => false
                    };

                    if (shouldNotify)
                    {
                        var level = entry.EventName == "PostToolUseFailure"
                            ? Views.NotificationPopup.NotificationLevel.Warning
                            : Views.NotificationPopup.NotificationLevel.Info;

                        ((NotificationService)notificationService).SendNotification(
                            entry.EventName, entry.Summary, level);
                    }
                }
            };
```

**Step 2: Build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/ClaudeTracker/App.xaml.cs
git commit -m "feat(hooks-v2): wire configurable notifications for hook events"
```

---

## Task 11: Update Release Workflow

**Files:**
- Modify: `.github/workflows/release.yml` (publish HookBridge alongside main app)

**Step 1: Add HookBridge publish step**

In `release.yml`, add a publish step for HookBridge after the main app publish:

```yaml
      - name: Publish HookBridge
        run: dotnet publish src/ClaudeTracker.HookBridge/ClaudeTracker.HookBridge.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/hookbridge
```

Add HookBridge to the release assets.

**Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add HookBridge to release workflow"
```

---

## Task 12: Tests

**Files:**
- Create: `tests/ClaudeTracker.Tests/HookEventDispatcherTests.cs`
- Create: `tests/ClaudeTracker.Tests/ActivityRecorderTests.cs`
- Create: `tests/ClaudeTracker.Tests/SessionTrackingServiceTests.cs`

**Step 1: Write dispatcher routing tests**

Test that:
- Interactive events route to correct handler
- Non-interactive events return success without handler
- All events broadcast to observers
- Unknown events don't crash

**Step 2: Write activity recorder tests**

Test `BuildSummary()` for each event type:
- Bash tool → "Bash: command"
- Edit tool → "Edit filepath"
- SessionStart → "Session started (startup)"
- etc.

**Step 3: Write session tracking tests**

Test:
- RegisterSession creates new session
- RegisterSession updates existing (resume)
- EndSession removes session
- PruneStale removes old sessions
- RecordActivity increments ToolCallCount

**Step 4: Run tests**

Run: `dotnet test`
Expected: All tests pass

**Step 5: Commit**

```bash
git add tests/
git commit -m "test(hooks-v2): add unit tests for dispatcher, activity recorder, and session tracking"
```

---

## Task Summary

| Task | Description | Estimated Steps |
|------|-------------|-----------------|
| 1 | Foundation: models, constants, settings | 6 |
| 2 | HookBridge console app | 5 |
| 3 | IPC service + interfaces | 9 |
| 4 | Dispatcher + observers | 7 |
| 5 | 7 interactive handlers | 4 |
| 6 | DI + startup wiring | 4 |
| 7 | Permission popup UI | 5 |
| 8 | Popover sessions + activity feed | 5 |
| 9 | Hooks settings tab | 7 |
| 10 | Notification wiring | 3 |
| 11 | Release workflow | 2 |
| 12 | Unit tests | 5 |

**Total: 12 tasks, ~62 steps**
