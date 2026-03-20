# Test Hooks Skill Design

## Overview

A developer testing skill (`/test-hooks`) that verifies ClaudeTracker's hooks integration end-to-end by sending synthetic events through HookBridge and validating delivery, notifications, and interactive responses.

## Architecture

Two components with clear separation:

- **`scripts/test-hooks.ps1`** — Standalone PowerShell script that sends all 21 hook events through HookBridge with realistic payloads. Runnable independently for quick smoke tests.
- **Skill `/test-hooks`** — Claude Code skill that orchestrates the script, verifies delivery via log inspection, handles interactive pauses, and formats a terminal summary table.

## Script: `scripts/test-hooks.ps1`

### Responsibilities

Send all 21 hook events through HookBridge in correct order with realistic payloads.

### Session Lifecycle

1. Send `SessionStart` (registers session + console window handle)
2. Send all other events using that session ID
3. Send `SessionEnd` to clean up

### Event Ordering

Events are sent in dependency order:
- `SubagentStart` before `SubagentStop`
- `PreToolUse` before `PostToolUse` / `PostToolUseFailure`
- `PreCompact` before `PostCompact`
- `Elicitation` before `ElicitationResult`
- `WorktreeCreate` before `WorktreeRemove`

### Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--interactive` | Pause on PermissionRequest and Elicitation for manual popup interaction | Off |
| `--bridge <path>` | Custom HookBridge path | Auto-detect from Release/Debug build |
| `--timeout <seconds>` | Wait time for interactive popups before auto-continuing | 10 |
| `--events <comma-list>` | Run only specific events (e.g., `PermissionRequest,Stop`) | All |

### Output

Each event prints a one-line status: `[SENT] EventName`. Interactive events also print response JSON from ClaudeTracker.

## Event Payloads

All payloads share `session_id`, `cwd` (current directory), and `hook_event_name`.

### Interactive Events (return response JSON)

| Event | Payload | Expected Response |
|-------|---------|-------------------|
| **PermissionRequest** | Bash tool: `npm install`, with `permission_suggestions` containing `addRules` suggestion | allow/deny decision |
| **Elicitation** | MCP server `TestServer` with `requestedSchema` containing text, single-select, and multi-select fields | User answers or timeout |
| **PreToolUse** | Read tool targeting a file path | Passthrough |
| **UserPromptSubmit** | Simulated user prompt text | Passthrough |
| **Stop** | `lastAssistantMessage` with task summary | Passthrough |
| **SubagentStop** | Agent ID matching prior SubagentStart | Passthrough |
| **ConfigChange** | Config file path | Passthrough |

### Observer Events (fire-and-forget)

| Event | Payload |
|-------|---------|
| **PostToolUse** | Bash tool with command + output |
| **PostToolUseFailure** | Bash tool with command + error message |
| **Notification (permission)** | Message containing "permission" keyword |
| **Notification (idle)** | Message containing "idle" keyword |
| **SubagentStart** | Agent type "Explore", agent ID |
| **PreCompact / PostCompact** | Session summary text |
| **WorktreeCreate / WorktreeRemove** | Worktree path |
| **InstructionsLoaded** | CLAUDE.md file path |
| **ElicitationResult** | Answers matching the Elicitation schema fields |
| **TeammateIdle** | Teammate session info |
| **TaskCompleted** | Task with last assistant message |
| **SessionStart / SessionEnd** | Session lifecycle bookends |

## Skill: `/test-hooks`

### Flow

1. **Pre-flight checks** — Verify ClaudeTracker is running (named pipe connection), verify HookBridge binary exists
2. **Snapshot log** — Record current log file line count to only check new entries
3. **Run script** — Execute PowerShell script, capture stdout
4. **Verify delivery** — For each event, grep log for `[HookIpc] Received event: <EventName>`
5. **Interactive pauses** — In `--interactive` mode, pause and instruct user to interact with popup
6. **Check notifications** — For notification-triggering events, verify `Notification sent:` in logs
7. **Print summary table**

### Result Table

```
Event                 Delivered  Notification  Response
─────────────────────────────────────────────────────
SessionStart          ✓          -             -
PermissionRequest     ✓          -             allow
Elicitation           ✓          -             submit
Notification (perm)   ✓          ✓             -
Stop                  ✓          ✓             -
...
─────────────────────────────────────────────────────
Result: 21/21 passed
```

### Failure Cases

| Condition | Display |
|-----------|---------|
| Event not in logs within 3s | `✗ not received` |
| Expected notification missing | `✗ notification missing` |
| HookBridge exits non-zero | `✗ bridge error` |

### Verification Mode

- **Default** — Log-based automated verification for all events. Interactive events use timeout (10s) then auto-continue.
- **`--interactive`** — Same log verification, but pauses on PermissionRequest and Elicitation so user can test the popup UI. Skill tells user what to expect and waits for confirmation.

## File Structure

```
scripts/
  test-hooks.ps1              # Standalone event sender
skills/
  test-hooks/
    test-hooks.md             # Skill definition (via skill-creator)
```

## Decisions

- **No new C# code** — Reuses existing HookBridge binary as-is
- **PowerShell** — Windows-native, no extra dependencies, matches the .NET/Windows stack
- **Log-based verification** — Non-invasive, doesn't require code changes to ClaudeTracker
- **Session-scoped suppression** — Tests benefit from the fix where notifications are only suppressed per-session, not globally
