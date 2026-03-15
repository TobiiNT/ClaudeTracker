# Hooks Audit Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix 7 bugs, 4 UX issues, and 4 code quality problems in the hooks system before merging feat/hooks-v2 to main.

**Architecture:** Two-pass approach — Pass 1 fixes all bugs/race conditions as one commit, Pass 2 addresses UX and code quality as a second commit. Each pass is independently revertable.

**Tech Stack:** .NET 8, WPF, C#, CommunityToolkit.Mvvm, xUnit + Moq

---

## Pass 1: Bug & Race Condition Fixes

### Task 1: Fix B7 — "Always Allow" sends wrong JSON format to Claude Code

This is the most user-visible bug. The response echoes the raw suggestion structure instead of the minimal format Claude Code expects.

**Files:**
- Modify: `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs:166-204`
- Test: `tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs` (create)

**Step 1: Write failing test**

Create `tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs`:

```csharp
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
        // Must NOT contain echoed suggestion fields
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
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "PermissionRequestHandlerTests.BuildResponseJson_AlwaysAllow" -v n`
Expected: FAIL — `type` is `"prefix"` not `"toolAlwaysAllow"`, and extra fields are present.

**Step 3: Fix BuildResponseJson**

In `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs`, replace lines 166-217 (the entire switch block):

```csharp
        switch (result.Decision)
        {
            case PermissionDecision.AlwaysAllow when result.AppliedSuggestion != null:
                decision[Response.Behavior] = Response.Allow;
                var permEntry = new JsonObject
                {
                    ["type"] = "toolAlwaysAllow",
                    ["tool"] = result.AppliedSuggestion.Tool
                };
                decision[Response.UpdatedPermissions] = new JsonArray { permEntry };
                break;

            case PermissionDecision.Allow:
            case PermissionDecision.AlwaysAllow: // no suggestion — degrade to one-time allow
                decision[Response.Behavior] = Response.Allow;
                break;

            case PermissionDecision.Deny:
                decision[Response.Behavior] = Response.Deny;
                break;
        }
```

**Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "PermissionRequestHandlerTests" -v n`
Expected: All 5 tests PASS.

**Step 5: Build to verify no compile errors**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 2: Fix B3 — TOCTOU race in PermissionRequestHandler

**Files:**
- Modify: `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs:45-69`

**Step 1: Write failing test**

Add to `tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs`:

```csharp
    [Fact]
    public void BuildResponseJson_AlwaysAllow_NoSuggestion_DegradesToAllow()
    {
        // When AlwaysAllow has no suggestion, should be same as Allow
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
```

**Step 2: Run test — should pass (already handled by Task 1)**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "BuildResponseJson_AlwaysAllow_NoSuggestion" -v n`
Expected: PASS

**Step 3: Fix the TOCTOU race**

In `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs`, replace lines 50-64:

```csharp
            var tcs = new TaskCompletionSource<HookResponse>();
            var args = new HookInteractiveEventArgs<PermissionRequestInfo>(info, tcs);

            // Capture delegate to avoid TOCTOU race (subscriber could unsubscribe between invoke and null check)
            var handler = PermissionRequested;
            handler?.Invoke(this, args);

            if (handler == null)
            {
                LoggingService.Instance.Log("PermissionRequestHandler: No UI handler subscribed, falling back to terminal");
                return new HookResponse
                {
                    RequestId = evt.RequestId,
                    Success = true,
                    JsonOutput = null
                };
            }
```

**Step 4: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 3: Fix B6 — Duplicate "Always allow" buttons

**Files:**
- Modify: `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs:564-621`

**Step 1: Add deduplication**

In `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs`, replace the `BuildAlwaysAllowButtons` method (lines 564-621):

```csharp
    private void BuildAlwaysAllowButtons(List<PermissionSuggestion> suggestions)
    {
        var seenLabels = new HashSet<string>();
        foreach (var suggestion in suggestions)
        {
            LoggingService.Instance.Log($"[PermPopup] Suggestion: type={suggestion.Type}, behavior={suggestion.Behavior}, tool={suggestion.Tool}, prefix={suggestion.Prefix}, rules={suggestion.Rules.Count}{(suggestion.Rules.Count > 0 ? $" [{suggestion.Rules[0].ToolName}:{suggestion.Rules[0].RuleContent}]" : "")}, dirs={suggestion.Directories.Count}");
            var label = suggestion.DisplayLabel;
            if (string.IsNullOrWhiteSpace(label) || !seenLabels.Add(label)) continue;
```

Only the first 3 lines of the method body change. The rest (lines 572-621) stays exactly the same.

**Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 4: Fix B1 + Q5 — Deadlock in SessionTrackingService

**Files:**
- Modify: `src/ClaudeTracker/Services/SessionTrackingService.cs` (full rewrite)

**Step 1: Rewrite with consistent dispatch-then-lock pattern**

Replace the entire content of `src/ClaudeTracker/Services/SessionTrackingService.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

/// <summary>
/// Manages active Claude Code sessions with thread-safe locking.
/// All methods use dispatch-then-lock pattern to prevent deadlocks:
/// Dispatcher.Invoke(() => { lock (_lock) { ... } })
/// </summary>
public class SessionTrackingService : ISessionTrackingService
{
    private readonly object _lock = new();

    public ObservableCollection<SessionState> ActiveSessions { get; } = new();
    public int ActiveSessionCount => ActiveSessions.Count;
    public event EventHandler? SessionsChanged;

    public void RegisterSession(string sessionId, string projectDirectory, string permissionMode, string? model)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var existing = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (existing != null)
                {
                    existing.CurrentActivity = "Resumed";
                    existing.LastActivityTime = DateTime.UtcNow;
                    return;
                }

                var session = new SessionState
                {
                    SessionId = sessionId,
                    Cwd = projectDirectory,
                    Model = model ?? ""
                };
                ActiveSessions.Add(session);
            }
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
                }
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void RecordActivity(string sessionId, ActivityEntry entry)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null) return;

                session.CurrentActivity = entry.Summary;
                session.LastActivityTime = DateTime.UtcNow;
                if (entry.ToolName != null) session.ToolCallCount++;

                session.Activities.Insert(0, entry);
                while (session.Activities.Count > Constants.Hooks.DefaultMaxActivityEntries)
                    session.Activities.RemoveAt(session.Activities.Count - 1);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void RegisterSubagent(string sessionId, string agentId, string? agentType)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null) return;
                if (!session.ActiveSubagents.Contains(agentId))
                {
                    session.ActiveSubagents.Add(agentId);
                    session.SubagentCount++;
                }
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void EndSubagent(string sessionId, string agentId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionId == sessionId);
                session?.ActiveSubagents.Remove(agentId);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void PruneStale()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Constants.Hooks.StaleSessionMinutes);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                var stale = ActiveSessions
                    .Where(s => s.LastActivityTime < cutoff)
                    .ToList();
                foreach (var s in stale) ActiveSessions.Remove(s);
                if (stale.Count > 0) SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }
}
```

**Step 2: Add LastActivityTime to SessionState**

In `src/ClaudeTracker/Models/HookModels.cs`, add after line 108 (`CurrentActivity` property):

```csharp
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
```

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 5: Fix B4 — Orphaned listener tasks on Stop()

**Files:**
- Modify: `src/ClaudeTracker/Services/HookIpcService.cs:69-85`

**Step 1: Replace Stop() method**

In `src/ClaudeTracker/Services/HookIpcService.cs`, replace lines 69-85:

```csharp
    public void Stop()
    {
        List<Task> tasksToAwait;
        lock (_lock)
        {
            if (!IsRunning) return;

            LoggingService.Instance.Log("[HookIpc] Stopping named pipe server");

            _cts?.Cancel();
            IsRunning = false;

            tasksToAwait = new List<Task>(_listeners);
            _listeners.Clear();
        }

        // Await outside lock to avoid holding lock during shutdown
        try
        {
            Task.WhenAll(tasksToAwait).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Expected — tasks cancelled
        }

        lock (_lock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
```

**Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 6: Fix B5 — Initialize() not thread-safe

**Files:**
- Modify: `src/ClaudeTracker/Services/HookEventDispatcher.cs:28-35`

**Step 1: Add lock**

In `src/ClaudeTracker/Services/HookEventDispatcher.cs`, replace lines 28-35:

```csharp
    private bool _initialized;
    private readonly object _initLock = new();

    public void Initialize()
    {
        lock (_initLock)
        {
            if (_initialized) return;
            _initialized = true;
            _ipcService.EventReceived += OnEventReceived;
        }
    }
```

**Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 7: Fix B2 — Add missing Constants for magic strings

**Files:**
- Modify: `src/ClaudeTracker/Utilities/Constants.cs:146-171`
- Modify: `src/ClaudeTracker/Services/Observers/SessionTracker.cs:36,44,50`

**Step 1: Add missing field constants**

In `src/ClaudeTracker/Utilities/Constants.cs`, add inside the `Fields` class after line 167 (`AgentType`):

```csharp
            public const string Model = "model";
            public const string AgentId = "agent_id";
```

**Step 2: Replace magic strings in SessionTracker**

In `src/ClaudeTracker/Services/Observers/SessionTracker.cs`:

Replace line 36: `json["model"]?.GetValue<string>());` with:
```csharp
                    json[Fields.Model]?.GetValue<string>());
```

Replace line 44: `var agentId = json["agent_id"]?.GetValue<string>() ?? "";` with:
```csharp
                var agentId = json[Fields.AgentId]?.GetValue<string>() ?? "";
```

Replace line 50: `var endAgentId = json["agent_id"]?.GetValue<string>() ?? "";` with:
```csharp
                var endAgentId = json[Fields.AgentId]?.GetValue<string>() ?? "";
```

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 8: Run all tests + commit Pass 1

**Step 1: Run full test suite**

Run: `dotnet test tests/ClaudeTracker.Tests -v n`
Expected: All tests pass.

**Step 2: Build Release**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

**Step 3: Commit Pass 1**

```bash
git add src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs \
        src/ClaudeTracker/Services/SessionTrackingService.cs \
        src/ClaudeTracker/Services/HookIpcService.cs \
        src/ClaudeTracker/Services/HookEventDispatcher.cs \
        src/ClaudeTracker/Services/Observers/SessionTracker.cs \
        src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs \
        src/ClaudeTracker/Models/HookModels.cs \
        src/ClaudeTracker/Utilities/Constants.cs \
        tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs
git commit -m "fix(hooks): race conditions, deadlock, and Always Allow response format

- B7: BuildResponseJson now emits {type: toolAlwaysAllow} format
- B1+Q5: SessionTrackingService uses dispatch-then-lock to prevent deadlock
- B2: PruneStale uses LastActivityTime instead of StartTime+CurrentActivity
- B3: PermissionRequestHandler captures delegate before invoking (TOCTOU fix)
- B4: HookIpcService.Stop() awaits listener tasks before clearing
- B5: HookEventDispatcher.Initialize() uses lock for thread safety
- B6: BuildAlwaysAllowButtons deduplicates by DisplayLabel
- Q3: SessionTracker uses Constants.Hooks.Fields instead of magic strings"
```

---

## Pass 2: UX & Code Quality

### Task 9: U1 — Async install/uninstall

**Files:**
- Modify: `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs:77-79,106-167`

**Step 1: Make RunBridgeCommand async and update click handlers**

In `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs`:

Replace lines 77-79 with:
```csharp
        InstallButton.Click += async (_, _) => { await RunBridgeCommandAsync("install"); _vm.CheckInstallStatus(); UpdateInstallUI(); };
        UninstallButton.Click += async (_, _) => { await RunBridgeCommandAsync("uninstall"); _vm.CheckInstallStatus(); UpdateInstallUI(); };
```

Remove the `OnInstallClick` and `OnUninstallClick` methods (lines 106-118).

Replace `RunBridgeCommand` (lines 131-167) with:

```csharp
    private async Task RunBridgeCommandAsync(string command)
    {
        var bridgePath = FindHookBridge();
        if (bridgePath == null)
        {
            InstallStatusText.Text = "HookBridge not found. Build the ClaudeTracker.HookBridge project first, or place it alongside ClaudeTracker.exe.";
            return;
        }

        InstallButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        InstallStatusText.Text = $"Running {command}...";

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                InstallStatusText.Text = process.ExitCode == 0
                    ? $"Success: {output.Trim()}"
                    : $"Error: {(string.IsNullOrEmpty(error) ? output : error).Trim()}";
            }
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = $"Failed to run HookBridge: {ex.Message}";
        }
    }
```

**Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 10: U2 — Clear activity feed button

**Files:**
- Modify: `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml:82-104`
- Modify: `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs` (add clear handler)

**Step 1: Add Clear button to XAML**

In `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml`, replace lines 82-91 (activity feed section header and toggle) with:

```xml
            <!-- Activity Feed -->
            <TextBlock Text="ACTIVITY FEED" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,8" />
            <Grid Margin="0,6,0,0">
                <StackPanel VerticalAlignment="Center" Margin="0,0,120,0">
                    <TextBlock Text="Show Activity Feed" FontSize="13" />
                    <TextBlock Text="Display recent hook events in the popover"
                               FontSize="11" Foreground="#999" TextWrapping="Wrap" Margin="0,2,0,0" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                    <Button x:Name="ClearFeedButton" Content="Clear" FontSize="10" Height="24"
                            Style="{StaticResource MaterialDesignOutlinedButton}" Margin="0,0,8,0" />
                    <ToggleButton x:Name="ActivityFeedToggle" Style="{StaticResource CompactSwitch}" />
                </StackPanel>
            </Grid>
```

**Step 2: Wire clear button in code-behind**

In `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs`, add after the activity feed toggle wiring (after line 66):

```csharp
        // Clear activity feed
        ClearFeedButton.Click += (_, _) =>
        {
            var activityService = App.Services.GetRequiredService<IActivityService>();
            activityService.Clear();
        };
```

Add the using at the top of the file:
```csharp
using ClaudeTracker.Services.Interfaces;
```

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 11: U3 — Slider trim existing entries on save

**Files:**
- Modify: `src/ClaudeTracker/Services/ActivityService.cs`
- Modify: `src/ClaudeTracker/Services/Interfaces/IActivityService.cs`
- Modify: `src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs:142-193`

**Step 1: Add TrimToMax to IActivityService and ActivityService**

In `src/ClaudeTracker/Services/Interfaces/IActivityService.cs`, add to the interface:

```csharp
    void TrimToMax(int max);
```

In `src/ClaudeTracker/Services/ActivityService.cs`, add after the `Clear()` method:

```csharp
    public void TrimToMax(int max)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            while (RecentFeed.Count > max)
                RecentFeed.RemoveAt(RecentFeed.Count - 1);
        });
    }
```

**Step 2: Call TrimToMax from Save()**

In `src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs`, add a constructor parameter and field:

Add field after line 11:
```csharp
    private readonly IActivityService _activityService;
```

Update constructor signature (line 46-48) to inject `IActivityService`:
```csharp
    public HooksSettingsViewModel(
        ISettingsService settingsService,
        IHookIpcService hookIpcService,
        IActivityService activityService)
    {
        _settingsService = settingsService;
        _hookIpcService = hookIpcService;
        _activityService = activityService;
```

In the `Save()` method, add after `_settingsService.Save();` (line 162):
```csharp
        // Trim existing activity feed to new limit
        _activityService.TrimToMax(MaxFeedEntries);
```

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 12: Q1 — Remove RawPayload from ActivityEntry

**Files:**
- Modify: `src/ClaudeTracker/Models/HookModels.cs:78-82`
- Modify: `src/ClaudeTracker/Services/Observers/ActivityRecorder.cs:41`

**Step 1: Remove RawPayload property from ActivityEntry**

In `src/ClaudeTracker/Models/HookModels.cs`, delete lines 78-80 (the RawPayload property):
```csharp
    [JsonPropertyName("rawPayload")]
    public string RawPayload { get; set; } = string.Empty;
```

**Step 2: Remove RawPayload assignment in ActivityRecorder**

In `src/ClaudeTracker/Services/Observers/ActivityRecorder.cs`, delete line 41:
```csharp
            RawPayload = evt.Payload
```

(Make sure the comma on line 40 `ProjectName = projectName` becomes the last property — remove its trailing comma if needed.)

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 13: Q2 — Bounded recursion in ParseJsonObjectToDictionary

**Files:**
- Modify: `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs:252-281`

**Step 1: Add depth limit**

Replace `ParseJsonObjectToDictionary` and `ParseJsonValue` methods (lines 252-281):

```csharp
    private static Dictionary<string, object> ParseJsonObjectToDictionary(JsonNode node, int depth = 0)
    {
        var dict = new Dictionary<string, object>();

        if (depth > 8 || node is not JsonObject obj)
            return dict;

        foreach (var kvp in obj)
        {
            dict[kvp.Key] = ParseJsonValue(kvp.Value, depth + 1);
        }

        return dict;
    }

    private static object ParseJsonValue(JsonNode? node, int depth = 0)
    {
        if (node == null) return string.Empty;
        if (depth > 8) return node.ToJsonString();

        return node switch
        {
            JsonObject obj => ParseJsonObjectToDictionary(obj, depth),
            JsonArray arr => arr.Select(n => ParseJsonValue(n, depth + 1)).ToList(),
            JsonValue val => val.TryGetValue<bool>(out var b) ? b
                : val.TryGetValue<long>(out var l) ? l
                : val.TryGetValue<double>(out var d) ? d
                : val.GetValue<string>() ?? string.Empty,
            _ => node.ToJsonString()
        };
    }
```

Also update the call site at line 103 — no change needed, the default parameter handles it.

**Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 14: Q4 — Remove unnecessary JsonPropertyName attributes

**Files:**
- Modify: `src/ClaudeTracker/Models/HookModels.cs:50-82,87-120`

**Step 1: Clean up ActivityEntry**

In `src/ClaudeTracker/Models/HookModels.cs`, remove `[JsonPropertyName(...)]` from every property in `ActivityEntry` (lines 50-82). The class should look like:

```csharp
public class ActivityEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public ActivityIcon Icon { get; set; }
    public string? ToolName { get; set; }
    public string? Detail { get; set; }
    public string ProjectName { get; set; } = string.Empty;
}
```

**Step 2: Clean up SessionState**

Remove `[JsonPropertyName(...)]` from every property in `SessionState` (lines 87-120). Keep `[JsonIgnore]` on `Activities` and `ProjectName` since those are still meaningful for any future serialization. The class should look like:

```csharp
public class SessionState
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public string Cwd { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int ToolCallCount { get; set; }
    public int SubagentCount { get; set; }
    public string CurrentActivity { get; set; } = string.Empty;
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
    public List<string> ActiveSubagents { get; set; } = new();

    [JsonIgnore]
    public ObservableCollection<ActivityEntry> Activities { get; set; } = new();

    [JsonIgnore]
    public string ProjectName => string.IsNullOrEmpty(Cwd)
        ? "Unknown"
        : System.IO.Path.GetFileName(Cwd) ?? Cwd;
}
```

**Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

---

### Task 15: Run all tests + commit Pass 2

**Step 1: Run full test suite**

Run: `dotnet test tests/ClaudeTracker.Tests -v n`
Expected: All tests pass.

**Step 2: Build Release**

Run: `dotnet build --configuration Release`
Expected: Build succeeded.

**Step 3: Commit Pass 2**

```bash
git add src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml \
        src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml.cs \
        src/ClaudeTracker/ViewModels/HooksSettingsViewModel.cs \
        src/ClaudeTracker/Services/ActivityService.cs \
        src/ClaudeTracker/Services/Interfaces/IActivityService.cs \
        src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs \
        src/ClaudeTracker/Services/Observers/ActivityRecorder.cs \
        src/ClaudeTracker/Models/HookModels.cs
git commit -m "refactor(hooks): UX polish and code quality improvements

- U1: Install/uninstall now async (no UI blocking)
- U2: Clear activity feed button
- U3: Slider trim existing entries on save
- Q1: Remove RawPayload from ActivityEntry (memory optimization)
- Q2: Bounded recursion depth (max 8) in ParseJsonObjectToDictionary
- Q4: Remove unnecessary JsonPropertyName attributes from non-serialized models"
```

---

## Note: U4 (data binding refactor) intentionally deferred

Replacing manual event wiring with XAML data binding (U4) requires WPF ToggleButton binding converters and would touch every toggle in the view. The risk-to-benefit ratio is too high for this audit. The current manual wiring works correctly — it's just verbose. Consider for a future cleanup pass.
