# Hooks System Audit — Bug Fixes, UX Polish, Code Quality

**Date**: 2026-03-15
**Branch**: `feat/hooks-v2`
**Scope**: Pre-merge audit — fix confirmed bugs, improve UX, clean up code quality

## Approach

**Option 1 (Risk-first, two passes)**:
- Pass 1: Fix all bugs and race conditions (B1-B7, Q5) as a standalone commit
- Pass 2: UX improvements and code quality cleanup in a second commit

## Pass 1 — Bug & Race Condition Fixes

### B1 + Q5: Deadlock in SessionTrackingService (CRITICAL)

**File**: `SessionTrackingService.cs`
**Problem**: `RecordActivity` holds `_lock` then calls `Dispatcher.Invoke` (blocking). If UI thread is simultaneously in `EndSession` or `PruneStale` waiting on `_lock`, classic WPF deadlock.
**Root cause**: Inconsistent locking — some methods lock-then-dispatch, others dispatch-then-lock.
**Fix**: Standardize all methods to dispatch-then-lock pattern: `Dispatcher.Invoke(() => { lock (_lock) { ... } })`. This ensures the UI thread never blocks on `_lock` while another thread holds `_lock` waiting for the dispatcher.

### B2: Stale sessions never pruned

**Files**: `HookModels.cs`, `SessionTrackingService.cs`
**Problem**: `PruneStale` checks `s.StartTime < cutoff && string.IsNullOrEmpty(s.CurrentActivity)`. But `CurrentActivity` is set to a non-empty summary on every `RecordActivity` call and never cleared. Sessions with any activity become immortal.
**Fix**: Add `LastActivityTime` (DateTime) to `SessionState`. Update it in `RecordActivity`. Change `PruneStale` to check `LastActivityTime < cutoff` instead of the StartTime + empty CurrentActivity condition.

### B3: TOCTOU race in PermissionRequestHandler

**File**: `PermissionRequestHandler.cs`
**Problem**: Line 53 invokes `PermissionRequested?.Invoke(this, args)`, then line 55 checks `if (PermissionRequested == null)`. A subscriber could unsubscribe between invoke and check, causing the handler to return without awaiting the TCS (popup shown but response never collected).
**Fix**: Capture the delegate to a local variable before invoking. Check the captured reference:
```csharp
var handler = PermissionRequested;
handler?.Invoke(this, args);
if (handler == null) { /* fall back to terminal */ }
```

### B4: Orphaned listener tasks on Stop()

**File**: `HookIpcService.cs`
**Problem**: `Stop()` cancels CTS and clears `_listeners` without awaiting tasks. If a handler TCS is mid-await, it may hang until GC.
**Fix**: In `Stop()`, cancel CTS, then `Task.WhenAll(_listeners).Wait(TimeSpan.FromSeconds(3))` before clearing. This gives in-flight handlers time to complete or cancel.

### B5: Initialize() not thread-safe

**File**: `HookEventDispatcher.cs`
**Problem**: `_initialized` flag read/set without synchronization. Could double-subscribe if `App.OnStartup` and Settings VM call `Initialize()` concurrently.
**Fix**: Add `lock` around the `_initialized` check-and-set:
```csharp
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

### B6: Duplicate "Always allow" buttons

**File**: `PermissionRequestPopup.xaml.cs`
**Problem**: `BuildAlwaysAllowButtons` iterates all suggestions and creates a button for each with no deduplication. When Claude Code sends suggestions with different internal structures but identical `DisplayLabel` values, duplicate buttons appear.
**Fix**: Track seen labels with `HashSet<string>` and skip duplicates:
```csharp
var seenLabels = new HashSet<string>();
foreach (var suggestion in suggestions)
{
    var label = suggestion.DisplayLabel;
    if (string.IsNullOrWhiteSpace(label) || !seenLabels.Add(label)) continue;
    // ... create button
}
```

### B7: "Always Allow" doesn't persist (MOST USER-VISIBLE)

**File**: `PermissionRequestHandler.cs`
**Problem**: `BuildResponseJson` echoes back the original suggestion structure from the input (`type: "prefix"`, extra `behavior`, `destination`, `rules`, etc.). Claude Code expects a minimal `{ "type": "toolAlwaysAllow", "tool": "ToolName" }` format for `updatedPermissions`. Claude Code silently ignores the unrecognized format and treats the response as a one-time allow.
**Fix**: Rewrite the `AlwaysAllow` case in `BuildResponseJson` to emit the correct format:
```csharp
case PermissionDecision.AlwaysAllow when result.AppliedSuggestion != null:
    decision[Response.Behavior] = Response.Allow;
    decision[Response.UpdatedPermissions] = new JsonArray
    {
        new JsonObject
        {
            ["type"] = "toolAlwaysAllow",
            ["tool"] = result.AppliedSuggestion.Tool
        }
    };
    break;
```

## Pass 2 — UX & Code Quality

### U1: Install/uninstall blocks UI thread

**File**: `HooksSettingsView.xaml.cs`
**Problem**: `RunBridgeCommand` calls `process.StandardOutput.ReadToEnd()`, `process.StandardError.ReadToEnd()`, `process.WaitForExit()` synchronously on UI thread.
**Fix**: Make `RunBridgeCommand` async. Use `process.WaitForExitAsync()` + `ReadToEndAsync()`. Update click handlers to be async.

### U2: No clear activity feed button

**Files**: `HooksSettingsView.xaml`, `HooksSettingsView.xaml.cs`
**Problem**: `ActivityService.Clear()` exists but no UI control invokes it.
**Fix**: Add "Clear" button next to activity feed toggle. Wire to `ActivityService.Clear()`.

### U3: Slider doesn't trim existing entries

**Files**: `HooksSettingsViewModel.cs`, `ActivityService.cs`
**Problem**: Reducing MaxFeedEntries only affects future insertions. Existing entries beyond the new limit remain visible.
**Fix**: Add `TrimToMax(int max)` method to `ActivityService`. Call it from `Save()` in the ViewModel after updating the setting.

### U4: Manual event wiring instead of data binding

**Files**: `HooksSettingsView.xaml`, `HooksSettingsView.xaml.cs`
**Problem**: 40+ lines of manual Checked/Unchecked event handlers for toggles. Fragile and verbose.
**Fix**: Replace with two-way `{Binding Path=PropertyName}` in XAML. Remove corresponding code-behind wiring.

### Q1: RawPayload memory overhead

**Files**: `HookModels.cs`, `ActivityRecorder.cs`
**Problem**: Every `ActivityEntry` stores the full JSON payload string. 200 entries/session * N sessions = significant memory.
**Fix**: Remove `RawPayload` property from `ActivityEntry`. Stop setting it in `ActivityRecorder.Observe`.

### Q2: Unbounded recursion in ParseJsonObjectToDictionary

**File**: `PermissionRequestHandler.cs`
**Problem**: No depth limit on recursive JSON parsing. Malformed payloads could stack overflow.
**Fix**: Add `maxDepth` parameter (default 8), decrement on each recursive call, return `node.ToJsonString()` at limit.

### Q3: Magic strings in SessionTracker

**Files**: `Constants.cs`, `SessionTracker.cs`
**Problem**: `json["model"]` and `json["agent_id"]` are not in `Constants.Hooks.Fields`.
**Fix**: Add `Model = "model"` and `AgentId = "agent_id"` to `Constants.Hooks.Fields`. Use them in `SessionTracker`.

### Q4: Unnecessary JsonPropertyName attributes

**File**: `HookModels.cs`
**Problem**: `ActivityEntry` and `SessionState` have `[JsonPropertyName]` attributes but are never serialized to disk.
**Fix**: Remove all `[JsonPropertyName]` attributes from `ActivityEntry` and `SessionState` classes.
