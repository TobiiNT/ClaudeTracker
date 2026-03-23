<#
.SYNOPSIS
    Sends all 21 Claude Code hook events through HookBridge for end-to-end testing.

.DESCRIPTION
    Standalone script that pipes synthetic hook event JSON through ClaudeTracker.HookBridge.exe
    to test the full IPC pipeline. Each event uses realistic payloads matching Claude Code's format.

.PARAMETER Interactive
    Pause on PermissionRequest and Elicitation events for manual popup interaction.

.PARAMETER Bridge
    Custom path to ClaudeTracker.HookBridge.exe. Auto-detects from build output if not specified.

.PARAMETER Timeout
    Seconds to wait for interactive popups before auto-continuing. Default: 10.

.PARAMETER Events
    Comma-separated list of specific events to test. Default: all events.

.EXAMPLE
    .\test-hooks.ps1
    .\test-hooks.ps1 -Interactive
    .\test-hooks.ps1 -Events "PermissionRequest,Stop"
#>

param(
    [switch]$Interactive,
    [string]$Bridge,
    [int]$Timeout = 10,
    [string]$Events
)

$ErrorActionPreference = "Stop"

# --- Auto-detect HookBridge ---
function Find-HookBridge {
    if ($Bridge -and (Test-Path $Bridge)) { return $Bridge }

    $candidates = @(
        "$PSScriptRoot\..\src\ClaudeTracker.HookBridge\bin\Release\net8.0\ClaudeTracker.HookBridge.exe",
        "$PSScriptRoot\..\src\ClaudeTracker.HookBridge\bin\Debug\net8.0\ClaudeTracker.HookBridge.exe"
    )
    foreach ($c in $candidates) {
        $resolved = Resolve-Path $c -ErrorAction SilentlyContinue
        if ($resolved) { return $resolved.Path }
    }
    return $null
}

$bridgePath = Find-HookBridge
if (-not $bridgePath) {
    Write-Host "[ERROR] HookBridge.exe not found. Build the project first or use -Bridge parameter." -ForegroundColor Red
    exit 1
}
Write-Host "[INFO] Using HookBridge: $bridgePath" -ForegroundColor Cyan

# --- Session setup ---
$sessionId = "test-" + [guid]::NewGuid().ToString().Substring(0, 8)
$cwd = (Get-Location).Path
$results = @()

# --- Event filter ---
$eventFilter = $null
if ($Events) {
    $eventFilter = $Events -split "," | ForEach-Object { $_.Trim() }
}

function Should-Run($eventName) {
    if (-not $eventFilter) { return $true }
    return $eventFilter -contains $eventName
}

# --- Send event helper ---
function Send-Event {
    param(
        [string]$EventName,
        [hashtable]$Payload,
        [switch]$IsInteractive
    )

    if (-not (Should-Run $EventName)) { return }

    $Payload["hook_event_name"] = $EventName
    if (-not $Payload.ContainsKey("session_id")) { $Payload["session_id"] = $sessionId }
    if (-not $Payload.ContainsKey("cwd")) { $Payload["cwd"] = $cwd }

    $json = $Payload | ConvertTo-Json -Depth 10 -Compress

    if ($Interactive -and $IsInteractive) {
        Write-Host "[WAIT] $EventName — interact with the popup, then press Enter..." -ForegroundColor Yellow
    }

    $response = $null
    try {
        $response = $json | & $bridgePath 2>&1
        $exitCode = $LASTEXITCODE
    } catch {
        $exitCode = 1
    }

    if ($exitCode -ne 0) {
        Write-Host "[FAIL] $EventName — exit code $exitCode" -ForegroundColor Red
        $script:results += [PSCustomObject]@{ Event = $EventName; Status = "FAIL"; Detail = "exit $exitCode" }
        return
    }

    $responseStr = if ($response) { ($response | Out-String).Trim() } else { "" }

    if ($IsInteractive -and $responseStr) {
        Write-Host "[SENT] $EventName -> $responseStr" -ForegroundColor Green
        $script:results += [PSCustomObject]@{ Event = $EventName; Status = "SENT"; Detail = $responseStr }
    } else {
        Write-Host "[SENT] $EventName" -ForegroundColor Green
        $script:results += [PSCustomObject]@{ Event = $EventName; Status = "SENT"; Detail = "" }
    }

    if ($Interactive -and $IsInteractive) {
        Read-Host "Press Enter to continue"
    }

    # Small delay between events to avoid overwhelming the pipe
    Start-Sleep -Milliseconds 300
}

# ============================================================
# EVENT SEQUENCE (dependency-ordered)
# ============================================================

Write-Host ""
Write-Host "=== ClaudeTracker Hook Test ===" -ForegroundColor Cyan
Write-Host "Session: $sessionId"
Write-Host "Mode: $(if ($Interactive) { 'Interactive' } else { 'Automated' })"
Write-Host ""

# 1. SessionStart
Send-Event "SessionStart" @{
    source = "cli"
    permission_mode = "default"
    model = "claude-sonnet-4-6"
}

# 2. UserPromptSubmit
Send-Event "UserPromptSubmit" @{
    prompt = "Test prompt: please list all files in the current directory"
}

# 3. PreToolUse (Read)
Send-Event "PreToolUse" @{
    tool_name = "Read"
    tool_input = @{ file_path = "$cwd\README.md" }
} -IsInteractive

# 4. PostToolUse (Read success)
Send-Event "PostToolUse" @{
    tool_name = "Read"
    tool_input = @{ file_path = "$cwd\README.md" }
}

# 5. PreToolUse (Bash) -> PermissionRequest
Send-Event "PreToolUse" @{
    tool_name = "Bash"
    tool_input = @{ command = "npm install express"; description = "Install express package" }
} -IsInteractive

# 6. PermissionRequest
Send-Event "PermissionRequest" @{
    tool_name = "Bash"
    tool_input = @{ command = "npm install express"; description = "Install express package" }
    permission_mode = "default"
    permission_suggestions = @(
        @{
            type = "addRules"
            behavior = "allow"
            destination = "user"
            tool = "Bash"
            prefix = ""
            rules = @(
                @{
                    toolName = "Bash"
                    ruleContent = "npm install*"
                }
            )
            directories = @()
        }
    )
} -IsInteractive

# 7. PostToolUse (Bash success)
Send-Event "PostToolUse" @{
    tool_name = "Bash"
    tool_input = @{ command = "npm install express"; description = "Install express package" }
}

# 8. PostToolUseFailure
Send-Event "PostToolUseFailure" @{
    tool_name = "Bash"
    tool_input = @{ command = "rm -rf /nonexistent"; description = "Delete nonexistent path" }
    tool_error = "rm: cannot remove '/nonexistent': No such file or directory"
}

# 9. InstructionsLoaded
Send-Event "InstructionsLoaded" @{
    file_path = "$cwd\CLAUDE.md"
}

# 10. SubagentStart
$agentId = "agent-" + [guid]::NewGuid().ToString().Substring(0, 8)
Send-Event "SubagentStart" @{
    agent_id = $agentId
    agent_type = "Explore"
}

# 11. SubagentStop
Send-Event "SubagentStop" @{
    agent_id = $agentId
    agent_type = "Explore"
} -IsInteractive

# 12. Elicitation (with text, single-select, multi-select fields)
$elicitationId = "elicit-" + [guid]::NewGuid().ToString().Substring(0, 8)
Send-Event "Elicitation" @{
    mcp_server_name = "TestServer"
    message = "Please configure the test parameters"
    mode = "interactive"
    url = ""
    elicitation_id = $elicitationId
    requested_schema = @{
        type = "object"
        properties = @{
            project_name = @{
                type = "string"
                description = "Name of the project to test"
                default = "ClaudeTracker"
            }
            log_level = @{
                type = "string"
                description = "Logging verbosity"
                enum = @("debug", "info", "warning", "error")
                default = "info"
            }
            features = @{
                type = "array"
                description = "Features to enable"
                items = @{
                    type = "string"
                    enum = @("notifications", "popups", "activity_feed", "session_tracking")
                }
            }
        }
        required = @("project_name")
    }
} -IsInteractive

# 13. ElicitationResult
Send-Event "ElicitationResult" @{
    elicitation_id = $elicitationId
    answers = @{
        project_name = "ClaudeTracker"
        log_level = "debug"
        features = @("notifications", "popups")
    }
}

# 14. PreCompact
Send-Event "PreCompact" @{
    summary = "Compacting conversation context..."
}

# 15. PostCompact
Send-Event "PostCompact" @{
    summary = "Context compacted successfully"
}

# 16. WorktreeCreate
Send-Event "WorktreeCreate" @{
    name = "test-worktree-branch"
}

# 17. WorktreeRemove
Send-Event "WorktreeRemove" @{
    name = "test-worktree-branch"
}

# 18. ConfigChange
Send-Event "ConfigChange" @{
    source = "settings"
    file_path = "$env:USERPROFILE\.claude\settings.json"
} -IsInteractive

# 19. Notification (permission keyword)
Send-Event "Notification" @{
    message = "Claude is waiting for your permission to run a command"
}

# 20. Notification (idle keyword)
Send-Event "Notification" @{
    message = "Claude has been idle for 5 minutes"
}

# 21. TeammateIdle
Send-Event "TeammateIdle" @{
    teammate_name = "test-teammate"
}

# 22. TaskCompleted
Send-Event "TaskCompleted" @{
    task_subject = "Test task completed successfully"
}

# 23. Stop
Send-Event "Stop" @{
    last_assistant_message = "All test events have been sent successfully."
    stop_hook_active = $true
} -IsInteractive

# 24. SessionEnd (cleanup)
Send-Event "SessionEnd" @{
    reason = "test_complete"
}

# ============================================================
# SUMMARY
# ============================================================

Write-Host ""
Write-Host "=== Results ===" -ForegroundColor Cyan
Write-Host ("{0,-25} {1,-8} {2}" -f "Event", "Status", "Detail") -ForegroundColor White
Write-Host ("-" * 60)
foreach ($r in $results) {
    $color = if ($r.Status -eq "SENT") { "Green" } else { "Red" }
    $detail = if ($r.Detail.Length -gt 40) { $r.Detail.Substring(0, 37) + "..." } else { $r.Detail }
    Write-Host ("{0,-25} {1,-8} {2}" -f $r.Event, $r.Status, $detail) -ForegroundColor $color
}
Write-Host ("-" * 60)

$passed = ($results | Where-Object { $_.Status -eq "SENT" }).Count
$total = $results.Count
$color = if ($passed -eq $total) { "Green" } else { "Red" }
Write-Host "Result: $passed/$total sent" -ForegroundColor $color
