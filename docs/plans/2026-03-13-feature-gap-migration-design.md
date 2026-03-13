# Feature Gap Migration Design: macOS → Windows

**Date:** 2026-03-13
**Status:** Approved
**Reference:** macOS Claude Usage Tracker at `D:\Projects\Claude-Usage-Tracker`

## Context

ClaudeTracker (Windows) is a port of the macOS Claude Usage Tracker. This design covers migrating proven features from the macOS reference (thousands of users) to the Windows app, maintaining stable behavior by porting verified algorithms while adapting the UI to WPF/Material Design.

## Decisions

| Question | Decision |
|----------|----------|
| Logic vs UI | **(B)** Port macOS business logic verbatim, adapt UI to WPF/Material Design |
| Explain items | **(B)** Document as "future work" specs in this design doc |
| XAML i18n | **(B)** Only fix views we're already modifying |
| WebView2 | **(C)** Optional — detect runtime, fallback to manual if absent |
| Charts library | **(A)** LiveCharts2 (future, planning only) |
| Engagement prompts | **(C)** Both star + feedback prompts, feedback endpoint configurable/placeholder |

## Implementation Approach

**Approach C: Dependency-Grouped Batches** — 5 independent batches, each a complete vertical slice (model → service → view → test).

---

## Batch 1: Usage Intelligence (#4, #2, #3)

### 1A. Effective Session Percentage (#4)

When the 5-hour session window has expired (reset time is in the past), the displayed percentage auto-zeros instead of showing stale data.

**ClaudeUsage.cs — new computed property:**
```csharp
[JsonIgnore]
public double EffectiveSessionPercentage =>
    SessionResetTime < DateTime.UtcNow ? 0.0 : SessionPercentage;
```

**RemainingPercentage** changes to use `EffectiveSessionPercentage`:
```csharp
[JsonIgnore]
public double RemainingPercentage => Math.Max(0, 100 - EffectiveSessionPercentage);
```

**Consumers to update:** `PopoverViewModel.RefreshData()`, `TrayIconRenderer`, `NotificationService.CheckAndNotify()`, `UsageRefreshCoordinator` — all switch from `SessionPercentage` to `EffectiveSessionPercentage`.

### 1B. Pace System (#2)

Projects end-of-period usage from current consumption rate. 6 tiers.

**New file: `Utilities/PaceStatus.cs`**

```csharp
public enum PaceStatus
{
    Comfortable = 0,  // projected <50%
    OnTrack = 1,      // projected 50-75%
    Warming = 2,      // projected 75-90%
    Pressing = 3,     // projected 90-100%
    Critical = 4,     // projected 100-120%
    Runaway = 5       // projected >120%
}
```

**Algorithm** (ported from macOS `PaceStatus.swift`):
```csharp
public static PaceStatus? Calculate(double usedPercentage, double elapsedFraction)
{
    if (elapsedFraction < 0.03 || elapsedFraction >= 1.0) return null;
    if (usedPercentage <= 0) return PaceStatus.Comfortable;

    var projected = (usedPercentage / 100.0) / elapsedFraction;
    return projected switch
    {
        < 0.50 => PaceStatus.Comfortable,
        < 0.75 => PaceStatus.OnTrack,
        < 0.90 => PaceStatus.Warming,
        < 1.00 => PaceStatus.Pressing,
        < 1.20 => PaceStatus.Critical,
        _      => PaceStatus.Runaway
    };
}
```

**Elapsed fraction calculation:**
- Session: `(now - (resetTime - 5h)) / 5h`
- Weekly: `(now - (resetTime - 7d)) / 7d`

**Color mapping (Material Design):**

| Pace | Hex | Name |
|------|-----|------|
| Comfortable | `#4CAF50` | Green |
| OnTrack | `#009688` | Teal |
| Warming | `#FFC107` | Amber |
| Pressing | `#FF9800` | Orange |
| Critical | `#F44336` | Red |
| Runaway | `#9C27B0` | Purple |

**Integration:** `PopoverViewModel` gains `SessionPaceStatus` and `WeeklyPaceStatus` properties. Popover shows a colored dot + label (e.g., "On Track") next to each usage card.

### 1C. Time Markers on Progress Bars (#3)

A thin vertical marker on each progress bar showing elapsed time position.

**Implementation:** In PopoverWindow.xaml, each progress bar gets an overlay — a 2px wide semi-transparent vertical line positioned at `elapsedFraction * barWidth`. Uses the same elapsed fraction as the pace system.

---

## Batch 2: Status & Resilience (#1, #22)

### 2A. Claude System Status Indicator (#1)

Live indicator of Claude's system health in the popover.

**New files:**
- `Models/ClaudeStatus.cs` — record with `StatusIndicator` enum (None, Minor, Major, Critical, Unknown) + `Description` string
- `Services/ClaudeStatusService.cs` + `Services/Interfaces/IClaudeStatusService.cs`

**API:** `GET https://status.claude.com/api/v2/status.json` — 10s timeout. Parses `status.indicator` string + `status.description`.

**Color mapping:**

| Indicator | Color | Meaning |
|-----------|-------|---------|
| None | Green | All Systems Operational |
| Minor | Yellow | Minor Issues |
| Major | Orange | Major Outage |
| Critical | Red | Critical Outage |
| Unknown | Gray | Unable to Fetch |

**Integration:**
- `UsageRefreshCoordinator` calls status service in parallel with usage fetch. Caches result, re-fetches every 5 minutes only.
- `PopoverViewModel` gains `ClaudeStatusIndicator` and `ClaudeStatusDescription` properties.
- Popover: colored dot + status text below profile switcher.

**Constants:**
```csharp
public static class StatusAPI
{
    public const string StatusUrl = "https://status.claude.com/api/v2/status.json";
    public const double StatusRefreshIntervalMinutes = 5.0;
}
```

### 2B. Network Connectivity Monitoring (#22)

Detects network loss/restoration, auto-refreshes on reconnect.

**New files:** `Services/NetworkMonitorService.cs` + `Services/Interfaces/INetworkMonitorService.cs`

**Implementation:** Uses `System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged` (built-in .NET). Fires `NetworkRestored` event after 3-second debounce on unavailable → available transition. `UsageRefreshCoordinator` subscribes and triggers immediate refresh.

No new dependencies.

---

## Batch 3: Auth & Expiry (#13, #21)

### 3A. Session Key Expiry Tracking (#13)

Tracks session key expiry, shows visual status, notifies 24 hours before.

**Profile.cs additions:**
```csharp
[JsonPropertyName("claudeSessionKeyExpiry")]
public DateTime? ClaudeSessionKeyExpiry { get; set; }

[JsonPropertyName("apiSessionKeyExpiry")]
public DateTime? ApiSessionKeyExpiry { get; set; }
```

**Expiry sources:**
- WebView2 browser sign-in (#21) extracts cookie `Expires` attribute
- CLI OAuth tokens: expiry already parsed from JWT in `ClaudeCodeSyncService`

**Visual indicator in PersonalUsageView:**
- Green: "Expires in X days" (>7 days)
- Orange: "Expires in X days" (1-7 days)
- Red: "Expires in X hours" (<24 hours)
- Gray: "No expiry data"

**Notification:** `NotificationService.CheckKeyExpiry(Profile)` fires Warning-level notification <24h before expiry. Tracked per-profile to avoid duplicates.

### 3B. WebView2 Browser Sign-in (#21)

Embedded Chromium browser for auto-extracting session cookies.

**New NuGet:** `Microsoft.Web.WebView2`

**New file:** `Views/BrowserSignInWindow.xaml(.cs)`

**Flow:**
1. User clicks "Sign in with Browser"
2. `BrowserSignInWindow` opens, navigates to login URL
3. Monitors `CoreWebView2.CookieManager` after each navigation
4. Extracts `sessionKey` cookie value + expiry when found
5. Returns `(sessionKey, expiry)` to caller, closes window

**Runtime detection:**
```csharp
try {
    var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
    hasWebView2 = !string.IsNullOrEmpty(version);
} catch { hasWebView2 = false; }
```

- Runtime present: "Sign in with Browser" (primary) + "Enter Key Manually" (secondary)
- Runtime absent: manual entry only + install hint

**Cookie targets:**

| Service | Navigate To | Cookie Domain |
|---------|------------|---------------|
| Claude.ai | `https://claude.ai/login` | `claude.ai` |
| Console API | `https://console.anthropic.com/login` | `platform.claude.com` |

**Important:** Console API session key is NOT part of the 3-tier usage auth fallback. It only provides billing data. Console fetch failures are non-fatal — the API card is simply hidden.

---

## Batch 4: Settings & Polish (#11, #12, #14, #16, #10)

### 4A. Popover Time Display Modes (#11)

**New enum in `Models/TimeFormatPreference.cs`:**
```csharp
public enum PopoverTimeDisplay
{
    ResetTime,      // "Resets Today 3:59 PM"
    RemainingTime,  // "Resets in 3h 45m" (current default)
    Both            // "Resets in 3h 45m (Today 3:59 PM)"
}
```

**AppSettings.cs addition:**
```csharp
[JsonPropertyName("popoverTimeDisplay")]
public string PopoverTimeDisplay { get; set; } = "remainingTime";
```

**FormatterHelper.cs additions:**
- `FormatResetTimeAbsolute(DateTime, bool use24Hour)` — "Today 3:59 PM" / "Tomorrow 10:30 AM"
- `FormatResetTimeCombined(DateTime, bool use24Hour)` — combined format

### 4B. 24-Hour Time Format (#12)

**New enum in `Models/TimeFormatPreference.cs`:**
```csharp
public enum TimeFormatPreference
{
    System,          // Follow Windows regional settings
    TwelveHour,      // Always 3:59 PM
    TwentyFourHour   // Always 15:59
}
```

**AppSettings.cs addition:**
```csharp
[JsonPropertyName("timeFormatPreference")]
public string TimeFormatPreference { get; set; } = "system";
```

System mode checks `CultureInfo.CurrentCulture.DateTimeFormat` for "H" pattern.

### 4C. Show Icon Names Toggle (#14)

Model property `ShowIconNames` already exists on `MenuBarIconConfiguration` (default true). `AppearanceViewModel` reads/writes it. But `TrayIconRenderer` never checks it.

**Fix:** `TrayIconRenderer` reads `ShowIconNames` when rendering multi-metric icons. Prepends "S:", "W:", "API:" text for Percentage and ProgressBar styles. Battery/Ring/Compact are too small for labels.

### 4D. XAML i18n for Modified Views (#16)

Replace hardcoded English with `{x:Static localization:Strings.Key}` bindings in views we're modifying:
- `PopoverWindow.xaml`
- `GeneralSettingsView.xaml`
- `PersonalUsageView.xaml`
- `AppearanceView.xaml`
- New views (BrowserSignInWindow, engagement prompts)

Add corresponding keys to `Strings.resx`.

### 4E. Wire Setup Wizard (#10)

`SetupWizardWindow` is fully implemented but never launched.

**Fix:** In `App.xaml.cs`, after DI initialization and before starting tray icon:
```csharp
if (!settings.HasCompletedSetup)
{
    var wizard = new SetupWizardWindow(/* inject services */);
    wizard.ShowDialog();
}
```

The wizard already sets `HasCompletedSetup = true` on completion/skip.

### 4F. Settings UI

New controls in `GeneralSettingsView` — "Time & Display" section:
- ComboBox for `PopoverTimeDisplay` (Reset Time / Remaining Time / Both)
- ComboBox for `TimeFormatPreference` (System / 12-Hour / 24-Hour)

---

## Batch 5: Engagement & Polish (#23, #24)

### 5A. Notification Sounds (#23)

**NotificationSettings.cs additions:**
```csharp
[JsonPropertyName("soundEnabled")]
public bool SoundEnabled { get; set; } = true;

[JsonPropertyName("soundName")]
public string SoundName { get; set; } = "Default";
```

**Available sounds** (built-in `System.Media.SystemSounds`, no new dependencies):
- Default (`Exclamation`), Hand, Asterisk, Question, Beep, None (silent)

`NotificationService.SendNotification()` plays selected sound after showing popup. `GeneralSettingsView` gains sound toggle + picker in Notifications section.

### 5B. GitHub Star Prompt (#24)

**AppSettings.cs additions:**
```csharp
[JsonPropertyName("hasStarredGitHub")]
public bool HasStarredGitHub { get; set; }

[JsonPropertyName("lastStarPromptDate")]
public DateTime? LastStarPromptDate { get; set; }

[JsonPropertyName("starPromptDismissedForever")]
public bool StarPromptDismissedForever { get; set; }
```

**New file:** `Views/GitHubStarPromptWindow.xaml(.cs)` — 400x250 modal. Star icon + "Enjoying Claude Tracker?" Three buttons: "Star on GitHub" (opens browser), "Remind Me Later", "Don't Ask Again".

**Trigger logic (after startup):**
```
if !hasStarred && !dismissedForever
   && firstLaunchDate != null
   && (now - firstLaunchDate) > 1 day
   && (lastPromptDate == null || (now - lastPromptDate) > 10 days)
→ show prompt
```

### 5C. Feedback Prompt (#24)

**AppSettings.cs additions:**
```csharp
[JsonPropertyName("hasSentFeedback")]
public bool HasSentFeedback { get; set; }

[JsonPropertyName("lastFeedbackPromptDate")]
public DateTime? LastFeedbackPromptDate { get; set; }

[JsonPropertyName("feedbackPromptDismissedForever")]
public bool FeedbackPromptDismissedForever { get; set; }
```

**New file:** `Views/FeedbackPromptWindow.xaml(.cs)` — 420x320 modal. 5-star rating + optional comment. Submit POST JSON: `{ rating, comment, version, os }`. No PII.

**Constants:**
```csharp
public static class Feedback
{
    public const string EndpointUrl = ""; // Set when backend is ready
    public const double PromptAfterDays = 7.0;
    public const double RemindIntervalDays = 14.0;
}
```

When `EndpointUrl` is empty, Submit is hidden — rating saved locally for future analytics.

**Trigger logic:** Same pattern as star prompt but after 7 days, 14-day remind interval, and never shown in the same session as the star prompt.

---

## File Impact Summary

| Batch | New Files | Modified Files |
|-------|-----------|----------------|
| 1. Usage Intelligence | `PaceStatus.cs` | `ClaudeUsage.cs`, `UsageStatusCalculator.cs`, `PopoverViewModel.cs`, `PopoverWindow.xaml`, `TrayIconRenderer.cs`, `NotificationService.cs` |
| 2. Status & Resilience | `ClaudeStatus.cs`, `ClaudeStatusService.cs`, `IClaudeStatusService.cs`, `NetworkMonitorService.cs`, `INetworkMonitorService.cs` | `UsageRefreshCoordinator.cs`, `PopoverViewModel.cs`, `PopoverWindow.xaml`, `App.xaml.cs`, `Constants.cs` |
| 3. Auth & Expiry | `BrowserSignInWindow.xaml(.cs)` | `Profile.cs`, `PersonalUsageView.xaml(.cs)`, `NotificationService.cs`, `ClaudeTracker.csproj` |
| 4. Settings & Polish | `TimeFormatPreference.cs` | `AppSettings.cs`, `FormatterHelper.cs`, `PopoverViewModel.cs`, `GeneralSettingsView.xaml`, `TrayIconRenderer.cs`, `App.xaml.cs`, `Strings.resx`, touched XAML views |
| 5. Engagement & Polish | `GitHubStarPromptWindow.xaml(.cs)`, `FeedbackPromptWindow.xaml(.cs)` | `NotificationSettings.cs`, `NotificationService.cs`, `AppSettings.cs`, `GeneralSettingsView.xaml`, `App.xaml.cs`, `Constants.cs` |

---

## Future Work

Documented here for later pickup. Not in current implementation scope.

### Multi-Profile Display Mode (#5)

macOS renders a separate NSStatusItem per profile marked `isSelectedForDisplay`. Windows options: (A) composite tray icon with stacked mini-bars, or (B) floating widget shows multi-profile data while tray shows active profile. Complexity: medium-high.

### Auto-Switch Profiles (#6)

macOS `ProfileManager` detects usage >= 100%, finds next profile with valid credentials, switches, re-syncs CLI credentials, notifies. Windows needs `ProfileService.AutoSwitchToNext()` triggered from `UsageRefreshCoordinator`. Add `AutoSwitchOnLimitEnabled` setting. Complexity: medium.

### Custom Notification Thresholds (#7)

macOS stores `customThresholds: [Int]` array alongside fixed 75/90/95. Merged + sorted descending for checking. Windows: add `CustomThresholds` list to `NotificationSettings`, iterate all in `CheckAndNotify`. Settings UI needs list editor with add/remove. Complexity: low-medium.

### Configurable Global Hotkeys (#8)

macOS `ShortcutManager` registers 4 Carbon hotkeys: toggle popover, refresh, settings, next profile. `ShortcutRecorderView` captures key combos. Windows: expand `GlobalHotkeyService` to support multiple configurable hotkeys, add recorder UI capturing `KeyDown` events, store in `AppSettings`. Complexity: medium.

### Usage History (#17) — Plan

**Architecture:**
- `Services/UsageHistoryService.cs` — DispatcherTimer snapshots (session/10min, weekly/2hr). Persists to `%APPDATA%\ClaudeTracker\usage_history.json` per profile. Auto-prunes (1000 session / 500 weekly max).
- `Models/UsageHistory.cs` — `UsageSnapshot`, `ResetType` enum, `UsageHistoryData` with filter/sort/export.
- `Views/Settings/UsageHistoryView.xaml` — LiveCharts2 `CartesianChart`, date range picker, export buttons.
- **NuGet:** `LiveChartsCore.SkiaSharpView.WPF`

### Claude Code Statusline (#18) — Plan

**Architecture:**
- `Services/StatuslineService.cs` — Generates PowerShell script reading usage cache file. Writes `~/.claude/.statusline-usage-cache` on each refresh. Manages `~/.claude/settings.json` statusline config.
- `Views/Settings/StatuslineView.xaml` — Component toggles, color mode picker, live terminal preview, one-click install.

### API Cost Breakdown (#19) — Plan

**Architecture:**
- Extend `ClaudeApiService` to call Console `/usage_cost?group_by=api_key_id` endpoint.
- New model: `APICostBreakdown` — per-model totals, per-key totals, daily totals, source type classification.
- Extend popover with expandable "API Details" section or dedicated view.

### Network Request Logger (#20)

macOS `NetworkLoggerService` provides timed capture (15min-12hr), logs URL/method/status/duration/body/response. Max 500 logs / 10MB. Filterable table UI with detail drill-down. Windows: wrap `HttpClient` with `LoggingDelegatingHandler : DelegatingHandler`, in-memory store with persistence, `DebugNetworkLogView` settings tab.

### Profile Badges (#25)

macOS shows small badge icons per profile indicating credential types: key (Claude.ai), terminal (CLI), credit card (Console). SF Symbols with green/gray color coding. Windows: Material Design `PackIcon` — `KeyVariant`, `Console`, `CreditCard` — accent when configured, gray when not. Show in popover ComboBox ItemTemplate and Profiles settings tab.
