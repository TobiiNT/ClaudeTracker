# Run this BEFORE recording to reset ClaudeTracker state
# Usage: powershell -ExecutionPolicy Bypass -File demo\reset-for-demo.ps1

$settingsPath = "$env:APPDATA\ClaudeTracker\settings.json"

if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath | ConvertFrom-Json

    # Reset onboarding so the dialog appears on next launch
    $settings.hooksOnboardingSeen = $false

    # Disable hooks (onboarding will re-enable them)
    $settings.hooksEnabled = $false

    # Enable all popup/notification features for demo
    $settings.hookPermissionPopupsEnabled = $true
    $settings.hookElicitationPopupsEnabled = $true
    $settings.hookActivityFeedEnabled = $true
    $settings.hookMaxFeedEntries = 15

    # Enable all notification types
    $settings.hookNotificationPreferences.stop = $true
    $settings.hookNotificationPreferences.toolError = $true
    $settings.hookNotificationPreferences.permission = $true
    $settings.hookNotificationPreferences.idle = $true
    $settings.hookNotificationPreferences.configChange = $true
    $settings.hookNotificationPreferences.sessionLifecycle = $true
    $settings.hookNotificationPreferences.subagent = $true

    $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
    Write-Host "Settings reset for demo recording." -ForegroundColor Green
    Write-Host "  - HooksOnboardingSeen = false (dialog will show)"
    Write-Host "  - HooksEnabled = false (onboarding will enable)"
    Write-Host "  - All notifications enabled"
    Write-Host ""
    Write-Host "Next: Close and relaunch ClaudeTracker" -ForegroundColor Yellow
} else {
    Write-Host "Settings file not found: $settingsPath" -ForegroundColor Red
}
