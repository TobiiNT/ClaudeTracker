---
name: test-hooks
description: End-to-end testing of ClaudeTracker's hooks integration. Use this skill whenever the user wants to test hooks, verify hook events are working, debug hook delivery issues, check if notifications/popups fire correctly, or validate the HookBridge IPC pipeline. Also trigger this when the user says things like "test the hooks", "are hooks working", "send test events", "verify notifications", or "check hook integration". Supports all 21 Claude Code hook event types with realistic payloads and log-based verification.
---

# Test Hooks

Orchestrate end-to-end testing of ClaudeTracker's hooks integration by sending all 21 hook events through HookBridge and verifying delivery via log inspection.

## How It Works

Two components work together:

1. **`scripts/test-hooks.ps1`** — Standalone PowerShell script that sends synthetic hook events through HookBridge with realistic payloads
2. **This skill** — Orchestrates the script, verifies delivery via ClaudeTracker logs, handles interactive pauses, and formats a summary table

The script can also be run independently for quick smoke tests without Claude Code.

## Prerequisites

Before running, verify:

1. **ClaudeTracker is running** — The app must be active with hooks enabled (Settings > Hooks > Enable)
2. **HookBridge exists** — Build the project first: `dotnet build --configuration Release`
3. **No permission popups blocking** — Close any pending permission popups from other sessions (they can suppress notifications for other sessions)

## Quick Start

Run from the project root:

```powershell
# All events, automated mode
powershell -ExecutionPolicy Bypass -File scripts/test-hooks.ps1

# Interactive mode — pauses on PermissionRequest and Elicitation popups
powershell -ExecutionPolicy Bypass -File scripts/test-hooks.ps1 -Interactive

# Test specific events only
powershell -ExecutionPolicy Bypass -File scripts/test-hooks.ps1 -Events "PermissionRequest,Elicitation,Stop"
```

## Verification Steps

After running the script, verify delivery by checking the ClaudeTracker log:

```powershell
# Get today's log file
$logFile = "$env:APPDATA\ClaudeTracker\logs\claudetracker-$(Get-Date -Format 'yyyyMMdd').log"

# Check which events were received
Select-String -Path $logFile -Pattern "\[HookIpc\] Received event:" | Select-Object -Last 30
```

For each event sent by the script, you should see a corresponding `[HookIpc] Received event: <EventName>` log entry.

### Notification verification

These events should trigger notification popups (if no permission popup is blocking):

| Event | Notification Condition |
|-------|----------------------|
| `Stop` / `TaskCompleted` | Settings > Hooks > Notify on task complete |
| `PostToolUseFailure` | Settings > Hooks > Notify on tool error |
| `Notification` (permission keyword) | Settings > Hooks > Notify on permission wait AND permission popups disabled |
| `Notification` (idle keyword) | Settings > Hooks > Notify on idle |

Check for `Notification sent:` entries in the log to confirm.

### Interactive event verification

These events show popup UI when permission popups are enabled:

| Event | Expected UI |
|-------|------------|
| `PermissionRequest` | Permission popup with Allow/Deny buttons and "Always Allow" suggestion |
| `Elicitation` | Input form with text field, dropdown (single-select), and checklist (multi-select) |

## Event Coverage

The script sends events in dependency order (24 sends covering all 21 event types, including 2 Notification variants and paired Pre/Post events):

### Session lifecycle
1. `SessionStart` — Registers test session with console window handle
2. `SessionEnd` — Cleanup (sent last)

### Tool events
3. `PreToolUse` (Read) — Passthrough handler
4. `PostToolUse` (Read) — Observer only
5. `PreToolUse` (Bash) — Passthrough handler
6. `PermissionRequest` (Bash) — Interactive: shows permission popup
7. `PostToolUse` (Bash) — Observer only
8. `PostToolUseFailure` — Triggers error notification

### User interaction
9. `UserPromptSubmit` — Passthrough handler

### Subagents
10. `SubagentStart` — Registers test subagent
11. `SubagentStop` — Interactive: passthrough handler

### Elicitation (question/answer)
12. `Elicitation` — Interactive: shows input form with 3 field types
13. `ElicitationResult` — Observer: carries answers back

### Context management
14. `PreCompact` — Observer only
15. `PostCompact` — Observer only

### Worktrees
16. `WorktreeCreate` — Observer only
17. `WorktreeRemove` — Observer only

### Configuration
18. `InstructionsLoaded` — Observer only
19. `ConfigChange` — Interactive: passthrough handler

### Notifications
20. `Notification` (permission) — Triggers permission-wait notification
21. `Notification` (idle) — Triggers idle notification

### Completion
22. `TeammateIdle` — Observer only
23. `TaskCompleted` — Triggers completion notification
24. `Stop` — Interactive: triggers completion notification

## Result Table Format

After verification, present results as:

```
Event                 Delivered  Notification  Response
-------------------------------------------------------------
SessionStart          pass       -             -
PermissionRequest     pass       -             allow
Elicitation           pass       -             submit
Notification (perm)   pass       pass          -
Stop                  pass       pass          -
...
-------------------------------------------------------------
Result: 21/21 passed
```

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| No events in log | ClaudeTracker not running or hooks disabled | Start app, enable hooks in Settings |
| `[FAIL]` in script output | HookBridge can't connect to named pipe | Ensure ClaudeTracker is running |
| Notifications missing | Permission popup blocking from another session | Close pending popups first |
| Elicitation popup empty | Schema fields not parsed | Check log for parse errors |
| Script hangs on interactive | Waiting for Enter key press | Press Enter after interacting with popup |

## Script Parameters Reference

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Interactive` | Switch | Off | Pause on PermissionRequest and Elicitation for manual popup testing |
| `-Bridge` | String | Auto-detect | Custom path to ClaudeTracker.HookBridge.exe |
| `-Timeout` | Int | 10 | Seconds to wait for interactive popups |
| `-Events` | String | All | Comma-separated list of specific events to test |
