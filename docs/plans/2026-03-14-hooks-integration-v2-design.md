# Claude Code Hooks Integration v2 — Design Document

**Date**: 2026-03-14
**Branch**: `feat/hooks-v2` (new branch from `main`, no dependency on `feat/hooks-integration`)
**Reference**: Commit `6a5d523` on `feat/hooks-integration` (read-only reference)

## Goal

Full coverage of all 21 Claude Code hook events with real-time activity monitoring, interactive permission/elicitation popups, and configurable notifications.

## Approach: Hybrid — Generic Transport + Typed Handlers

- Generic IPC transport using raw `hook_event_name` strings (no enum)
- Typed handlers for 7 interactive events that need UI and structured responses
- Generic observers for activity recording and session tracking (see all events, never block)
- Future-proof: new Claude Code events auto-forwarded without code changes

---

## 1. IPC & Transport Layer

### HookBridge (Console App)

Thin relay with zero event-specific logic:

1. Reads Claude Code JSON from stdin
2. Connects to named pipe `ClaudeTracker-Hooks-{UserName}`
3. Wraps in envelope: `{ requestId, eventName, payload, timestamp }`
4. Sends via 4-byte length-prefixed binary protocol
5. Reads response, writes `jsonOutput` to stdout
6. If ClaudeTracker not running → exits silently (Claude Code falls back to terminal)

Modes: `hook` (default), `install`, `uninstall`, `status`

### IPC Envelope

```json
{
  "requestId": "guid",
  "eventName": "PreToolUse",
  "payload": "{ ... raw Claude Code stdin JSON ... }",
  "timestamp": "2026-03-14T12:00:00Z"
}
```

Response: `{ requestId, success, jsonOutput }` — jsonOutput is the raw JSON written to stdout.

---

## 2. Event Dispatcher & Handler Architecture

### Routing

```
Incoming HookEvent
    │
    ├─ Is interactive? (needs response back to Claude Code)
    │   ├─ PermissionRequest  → PermissionHandler
    │   ├─ PreToolUse         → PreToolUseHandler
    │   ├─ Elicitation        → ElicitationHandler
    │   ├─ UserPromptSubmit   → UserPromptHandler
    │   ├─ Stop               → StopHandler
    │   ├─ SubagentStop       → SubagentStopHandler
    │   └─ ConfigChange       → ConfigChangeHandler
    │
    ├─ Is session lifecycle?
    │   ├─ SessionStart       → SessionTracker.Start()
    │   ├─ SessionEnd         → SessionTracker.End()
    │   └─ Stop (also)        → SessionTracker.MarkIdle()
    │
    └─ ALL events             → ActivityRecorder.Record()
```

### Interfaces

```csharp
interface IHookEventHandler
{
    bool CanHandle(string eventName);
    Task<HookResponse> HandleAsync(HookEvent evt);
}

interface IHookEventObserver
{
    void Observe(HookEvent evt);
}
```

Handlers registered via DI. Adding a new interactive event = one handler class + DI registration.

---

## 3. Data Models

### Generic Core

```csharp
HookEvent { RequestId, EventName (string), Payload (string), Timestamp }
HookResponse { RequestId, Success, JsonOutput }

ActivityEntry {
    Id, SessionId, EventName, Timestamp,
    Summary (string),        // "Edit src/App.cs", "Bash: npm test"
    Icon (enum),             // Tool, Permission, Session, Subagent, System
    ToolName?,
    RawPayload (string)
}
```

### Typed Models (interactive handlers only)

```csharp
// Permission (includes AskUserQuestion special case)
PermissionRequestInfo { ToolName, ToolInput, PermissionSuggestions, SessionId, Cwd, PermissionMode }
PermissionDecisionResult { Decision, AppliedSuggestion, UpdatedInput }

// PreToolUse
PreToolUseInfo { ToolName, ToolInput, ToolUseId, SessionId, Cwd }
PreToolUseResult { PermissionDecision, UpdatedInput, AdditionalContext }

// Elicitation (MCP)
ElicitationInfo { McpServerName, Message, Mode, RequestedSchema, ElicitationId }
ElicitationResult { Action, Content }

// Simple interactive
UserPromptSubmitInfo { Prompt, SessionId }
StopInfo { LastAssistantMessage, StopHookActive, SessionId }
SubagentStopInfo { AgentId, AgentType, LastAssistantMessage }
ConfigChangeInfo { Source, FilePath }
```

### Session State

```csharp
SessionState {
    SessionId, ProjectDirectory, ProjectName,
    StartedAt, LastActivityAt, IsActive,
    PermissionMode, Model,
    ToolCallCount, SubagentCount,
    Activities (ObservableCollection<ActivityEntry>),
    ActiveSubagents (list)
}
```

### Special: AskUserQuestion

Handled inside PermissionRequestHandler. When `ToolName == "AskUserQuestion"`, popup renders questions/options from `ToolInput` and packs selected answers into `PermissionDecisionResult.UpdatedInput`.

---

## 4. UI Design

### Popover Additions

```
┌──────────────────────────────┐
│ Profile Switcher    [↻]      │  existing
├──────────────────────────────┤
│ Session Usage          72%   │  existing
│ Weekly Usage           34%   │  existing
├──────────────────────────────┤
│ ◉ Active Sessions (2)       │  enhanced
│ ┌────────────────────────┐   │
│ │ 🟢 claude-tracker  3m  │   │  click → session detail
│ │    Edit App.cs     12⚡ │   │  current activity + tool count
│ ├────────────────────────┤   │
│ │ 🟢 api-server     18m  │   │
│ │    Bash: npm test   5⚡ │   │
│ └────────────────────────┘   │
├──────────────────────────────┤
│ Recent Activity              │  new: scrolling feed
│  ⚡ Edit src/App.cs    12s   │
│  ⚡ Bash: npm test     28s   │
│  🔔 Permission Needed  1m   │
│  ▶ Subagent: Explore   2m   │
└──────────────────────────────┘
│ Claude Tracker    [⊞][⚙][⏻] │  existing
└──────────────────────────────┘
```

- **Session detail**: click session → expand inline with full activity history + stats
- **Permission popup**: same pattern as reference, with AskUserQuestion questions/options + diff/write previews
- **Elicitation popup**: renders MCP `requested_schema` as dynamic form

### Configurable Notifications

```
Hooks Settings
├─ [✓] Enable Hooks Integration
├─ [✓] Permission Popups
├─ [✓] Elicitation Popups
│
├─ Notifications
│   [✓] Task Complete (Stop)
│   [✓] Tool Errors (PostToolUseFailure)
│   [✓] Permission Needed
│   [✓] Idle / Waiting for Input
│   [ ] Config Changes
│   [ ] Session Start/End
│   [ ] Subagent Start/Stop
│
├─ Activity Feed
│   [✓] Show in Popover
│   Max entries: [10 ▾]
│
└─ Install / Uninstall Hooks
```

Stored as `Dictionary<string, bool> HookNotificationPreferences` in AppSettings.

---

## 5. Service Registration & Lifecycle

### DI (in ConfigureServices)

```csharp
// Core IPC
services.AddSingleton<IHookIpcService, HookIpcService>();
services.AddSingleton<IHookEventDispatcher, HookEventDispatcher>();

// Interactive handlers (7)
services.AddSingleton<IHookEventHandler, PermissionRequestHandler>();
services.AddSingleton<IHookEventHandler, PreToolUseHandler>();
services.AddSingleton<IHookEventHandler, ElicitationHandler>();
services.AddSingleton<IHookEventHandler, UserPromptHandler>();
services.AddSingleton<IHookEventHandler, StopHandler>();
services.AddSingleton<IHookEventHandler, SubagentStopHandler>();
services.AddSingleton<IHookEventHandler, ConfigChangeHandler>();

// Observers
services.AddSingleton<IHookEventObserver, ActivityRecorder>();
services.AddSingleton<IHookEventObserver, SessionTracker>();

// UI state
services.AddSingleton<IActivityService, ActivityService>();
services.AddSingleton<ISessionTrackingService, SessionTrackingService>();
```

### Startup

1. Read `HooksEnabled` from settings
2. If enabled → `HookIpcService.Start()` → named pipe listener
3. `HookEventDispatcher` subscribes to `HookIpcService.EventReceived`
4. Dispatcher routes to handlers + observers

### Runtime Toggle

Settings toggle → actually calls `Start()`/`Stop()` on IPC service.

### Cleanup

- `SessionTracker` prunes stale sessions (15 min no activity)
- `ActivityService` caps entries per session (configurable, default 200)
- App shutdown → `HookIpcService.Stop()` disposes pipe

---

## 6. HookBridge Installation

Registers all 21 events in `~/.claude/settings.json`:

- Non-interactive events use `"async": true` (don't slow Claude)
- `SessionEnd` gets `"timeout": 2` (Claude caps at 1.5s)
- Install uses absolute path to `HookBridge.exe`
- Merge-safe: reads existing settings, adds/updates only ClaudeTracker entries
- Uninstall removes only ClaudeTracker entries, preserves user's other hooks

---

## 7. File Inventory

### New Files (~15)

| File | Purpose |
|------|---------|
| `src/ClaudeTracker.HookBridge/Program.cs` | Thin pipe relay (rewritten) |
| `src/ClaudeTracker/Models/HookModels.cs` | Generic + typed models (rewritten) |
| `src/ClaudeTracker/Services/HookIpcService.cs` | Named pipe server (rewritten) |
| `src/ClaudeTracker/Services/HookEventDispatcher.cs` | Central event router |
| `src/ClaudeTracker/Services/Handlers/PermissionRequestHandler.cs` | Permission popup + AskUserQuestion |
| `src/ClaudeTracker/Services/Handlers/PreToolUseHandler.cs` | Pre-tool interception |
| `src/ClaudeTracker/Services/Handlers/ElicitationHandler.cs` | MCP elicitation form |
| `src/ClaudeTracker/Services/Handlers/UserPromptHandler.cs` | Prompt gate |
| `src/ClaudeTracker/Services/Handlers/StopHandler.cs` | Stop override |
| `src/ClaudeTracker/Services/Handlers/SubagentStopHandler.cs` | Subagent stop override |
| `src/ClaudeTracker/Services/Handlers/ConfigChangeHandler.cs` | Config change gate |
| `src/ClaudeTracker/Services/ActivityRecorder.cs` | Activity feed + history |
| `src/ClaudeTracker/Services/SessionTracker.cs` | Session lifecycle |
| `src/ClaudeTracker/Views/ElicitationPopup.xaml[.cs]` | MCP form popup |
| `src/ClaudeTracker/Views/PermissionRequestPopup.xaml[.cs]` | Permission popup (rewritten) |

### Modified Files

| File | Change |
|------|--------|
| `App.xaml.cs` | DI registration, startup wiring |
| `Models/AppSettings.cs` | Hooks settings + notification preferences |
| `ViewModels/PopoverViewModel.cs` | Activity feed + session state bindings |
| `Views/PopoverWindow.xaml[.cs]` | Sessions card, activity feed, session detail |
| `Views/Settings/HooksSettingsView.xaml[.cs]` | Notification toggles, activity config |
| `ViewModels/HooksSettingsViewModel.cs` | New notification preference bindings |
| `Utilities/Constants.cs` | Pipe name, timeouts |
