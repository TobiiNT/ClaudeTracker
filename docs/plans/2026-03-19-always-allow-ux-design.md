# Always-Allow UX Improvement Design

**Date**: 2026-03-19
**Status**: Approved

## Problem

1. **Rules too narrow** — Claude Code's permission suggestions are overly specific (e.g., flags safe compound `cd && git` commands)
2. **No visibility** — no way to see what always-allow rules exist in `~/.claude/settings.json`
3. **No bulk approve** — can't say "always allow this tool" without waiting for the right suggestion

## Solution

Two changes:

### 1. Permissions Tab in Settings

New settings tab that reads `~/.claude/settings.json` and displays all permission rules.

**Capabilities**:
- View all rules with human-readable labels (reuse `PermissionSuggestion.DisplayLabel` logic)
- Delete individual rules (remove from JSON, write back to file)
- Refresh button to re-read file

**UI**: Flat list with delete button per row. No create/edit — keep it simple.

**Data flow**:
- Read: Parse `~/.claude/settings.json` → extract permission entries → display
- Delete: Remove entry from JSON → write back to `~/.claude/settings.json`

### 2. Enhanced Permission Popup — Blanket Tool Allow Button

Add one extra "Always allow [Tool]" button to the permission popup alongside Claude Code's existing suggestions.

**Behavior**:
- Uses `toolAlwaysAllow` type with just the tool name (no rules/paths)
- Only added if Claude Code's suggestions don't already include a blanket tool allow
- Appears last (broadest option) with same blue styling

**Example**: For a Bash permission request with suggestion "Always allow git:*", the popup shows:
- `[Always allow git:*]` — Claude Code's suggestion (narrow)
- `[Always allow Bash]` — New blanket option (broad)

## Files to Change

- **New**: `Views/Settings/PermissionsSettingsView.xaml` + `.xaml.cs` — Permissions tab UI
- **New**: `ViewModels/Settings/PermissionsSettingsViewModel.cs` — Tab ViewModel
- **New**: `Services/Interfaces/IClaudeCodeSettingsService.cs` — Interface for reading/writing Claude Code's settings.json
- **New**: `Services/ClaudeCodeSettingsService.cs` — Read/write `~/.claude/settings.json` permissions
- **Modify**: `Views/PermissionRequestPopup.xaml.cs` — Add blanket tool allow button in `BuildAlwaysAllowButtons()`
- **Modify**: `Views/Settings/SettingsWindow.xaml` — Add Permissions tab
- **Modify**: `App.xaml.cs` — Register new services and ViewModels in DI

## Non-Goals

- No custom rule editor/builder
- No rule creation from scratch (only via popup)
- No import/export
- No path-scoped blanket rules (e.g., "Allow Edit in src/")
