# ClaudeTracker

Windows system tray application for real-time Claude AI usage monitoring and Claude Code hooks integration.

## Tech Stack

- **.NET 8.0** (net8.0-windows10.0.19041.0), **WPF**, **C#**
- **MaterialDesignThemes 5.1.0** — Material Design UI
- **CommunityToolkit.Mvvm 8.4.0** — MVVM pattern (`[ObservableProperty]`, `[RelayCommand]`)
- **SkiaSharp 3.116.1** — DPI-aware tray icon rendering
- **Hardcodet.NotifyIcon.Wpf 1.1.0** — System tray integration
- **Serilog 4.2.0** — Structured logging to file
- **xUnit 2.9.2 + Moq 4.20.72** — Unit testing

## Project Structure

```
src/ClaudeTracker/
├── Models/          # Data models (Profile, ClaudeUsage, APIUsage, AppSettings, etc.)
├── Services/        # Business logic with interfaces in Services/Interfaces/
│   ├── Handlers/    # 7 interactive hook event handlers
│   └── Observers/   # ActivityRecorder, SessionTracker (fire-and-forget)
├── ViewModels/      # MVVM ViewModels (CommunityToolkit.Mvvm)
├── Views/           # WPF XAML windows and user controls
│   ├── Controls/    # Reusable UI controls
│   └── Settings/    # Settings tab views (9 tabs, incl. Hooks)
├── TrayIcon/        # TrayIconManager + TrayIconRenderer (SkiaSharp)
├── Utilities/       # Constants, FormatterHelper, PopupStackManager, TerminalFocusHelper, etc.
├── Themes/          # SharedStyles.xaml (Material Design)
├── Localization/    # Strings.resx (i18n)
└── Assets/          # app_icon.ico
src/ClaudeTracker.HookBridge/  # CLI relay: stdin → named pipe → ClaudeTracker
tests/ClaudeTracker.Tests/  # xUnit tests
```

## Architecture

- **MVVM** with full **Dependency Injection** (Microsoft.Extensions.DependencyInjection)
- Services registered as **Singleton**, settings ViewModels as **Transient**
- All DI wiring in `App.xaml.cs → ConfigureServices()`
- Single-instance enforcement via Mutex

### Key Services

| Service | Purpose |
|---------|---------|
| `ClaudeApiService` | API calls to claude.ai, console.anthropic.com, api.anthropic.com |
| `ProfileService` | Multi-profile CRUD, active profile switching |
| `SettingsService` | JSON settings persistence (`%APPDATA%\ClaudeTracker\settings.json`) |
| `CredentialService` | CLI credentials (`~/.claude/.credentials.json`) |
| `NotificationService` | Usage alerts at 75%/90%/95% thresholds, hook event notifications |
| `UsageRefreshCoordinator` | DispatcherTimer-based polling (default 60s), rate-limit backoff, auto-refresh on session reset |
| `ClaudeCodeSyncService` | CLI OAuth token sync + silent refresh (POSTs to `OAuthTokenEndpoint` with stored refresh token) |
| `AutoStartSessionService` | Auto-start session when usage resets to 0% |
| `TrayIconManager` | System tray lifecycle, popover positioning, context menu |
| `TrayIconRenderer` | SkiaSharp rendering for 5 icon styles (Battery, ProgressBar, Percentage, Ring, Compact) |
| `HookIpcService` | Named pipe server (IPC) receiving events from HookBridge |
| `HookEventDispatcher` | Routes events to handlers + broadcasts to observers |
| `ActivityService` | Recent activity feed (ObservableCollection, UI-thread dispatched) |
| `SessionTrackingService` | Active session lifecycle, tool counts, subagent tracking |
| `PermissionRequestHandler` | Parses permission events, shows popup, returns decision to Claude Code |
| `ElicitationHandler` | MCP server input request popup |

### Authentication (3-tier fallback in ClaudeApiService)

1. Claude.ai session key (cookie-based)
2. CLI OAuth token (Bearer, from saved profile or `~/.claude/.credentials.json`)
3. API Console session key (billing only)

**Silent OAuth refresh**: When access token is expired but refresh token exists, `ClaudeCodeSyncService.TryRefreshTokenAsync()` POSTs to `Constants.APIEndpoints.OAuthTokenEndpoint` and updates `~/.claude/.credentials.json`. Runs automatically for the **default profile only** (during poll + AutoDetect). Additional profiles must trigger it explicitly via Connect.

### Hooks Integration (v2.0.0+)

- **IPC**: Named pipe (`ClaudeTracker-Hooks-{UserName}`), 4-byte length-prefix + UTF-8 JSON
- **HookBridge**: Standalone .exe relayed by Claude Code → reads stdin → sends to pipe → writes stdout
- **Event flow**: Claude Code → HookBridge (stdin) → Named Pipe → HookIpcService → HookEventDispatcher → Handlers + Observers
- **21 hook events** covered, 7 interactive (PermissionRequest, PreToolUse, Elicitation, etc.)
- **Version-gated install**: HookBridge detects `claude --version` and skips unsupported events
- **Always Allow response**: echoes raw suggestion JSON verbatim (don't reconstruct — fields get lost)
- **Popup positioning**: PopupStackManager reads monitor + corner from settings, DPI-aware
- **Thread safety**: SessionTrackingService uses dispatch-then-lock pattern (never lock-then-dispatch — causes WPF deadlock)

## Build & Test

```bash
# Build
dotnet build --configuration Release

# Test
dotnet test

# Publish (self-contained single EXE)
dotnet publish src/ClaudeTracker/ClaudeTracker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Publish (framework-dependent, smaller)
dotnet publish src/ClaudeTracker/ClaudeTracker.csproj -c Release -r win-x64 --no-self-contained

# Install hooks into Claude Code
src/ClaudeTracker.HookBridge/bin/Release/net8.0/ClaudeTracker.HookBridge.exe install

# Check hook installation status
src/ClaudeTracker.HookBridge/bin/Release/net8.0/ClaudeTracker.HookBridge.exe status
```

Note: Debug build will fail if the app is running (file lock). Use Release config or close the app first.

## Key File Paths (Runtime)

- Settings: `%APPDATA%\ClaudeTracker\settings.json`
- Logs: `%APPDATA%\ClaudeTracker\logs\claudetracker-.log`
- CLI Credentials: `%USERPROFILE%\.claude\.credentials.json`
- Claude Code hooks config: `%USERPROFILE%\.claude\settings.json`

## Conventions

- All ViewModels use `[ObservableProperty]` and `[RelayCommand]` attributes from CommunityToolkit.Mvvm
- Service interfaces live in `Services/Interfaces/` — always program to the interface
- Settings are camelCase JSON with thread-safe locking in SettingsService
- Tray icon rendering is DPI-aware (Win32 `GetDpiForSystem`)
- Usage status levels: Safe (green, >20% remaining), Moderate (orange, 10-20%), Critical (red, <10%)
- Constants centralized in `Utilities/Constants.cs`
- HookBridge must use `Console.InputEncoding = Encoding.UTF8` for non-ASCII (Vietnamese, CJK)
- Permission popup fonts: Segoe UI primary (Unicode support), Consolas fallback for code
- `BuildResponseJson` for AlwaysAllow: use raw JSON echo for rule-based (Bash), `toolAlwaysAllow` only if no rules
- `UsageRefreshCoordinator` has `_isRefreshing` guard + 5-min rate-limit backoff — don't remove these
- Notification `SendNotification` accepts optional `cwd` param — when set, click focuses terminal instead of popover
- MCP tool names: `mcp__Server__action_Target` format — `FormatMcpToolName` strips prefix for display
- Connection UX: default/single profile uses unified **Auto Detect** (CLI → silent refresh → error); additional profiles show explicit **CLI / Browser / Manual** buttons — don't collapse these into AutoDetect
- WebView2 sign-in uses persistent `UserDataFolder` at `Constants.WebView2.ProfilePath` — users sign in once; don't omit `UserDataFolder` or they re-authenticate every time
- `ReleaseSingleInstanceMutex()` is called by both UpdateService (pre-restart) and OnExit — catch `ApplicationException` to guard against the second call; don't remove this guard

## Git Conventions

### Branch Names
Format: `<type>/<short-description>` using kebab-case.
- **feat/** — new feature (`feat/personal-api-usage`, `feat/connection-ux-redesign`)
- **fix/** — bug fix (`fix/onboarding-and-browser-login`, `fix/connect-ux-redesign`)
- **refactor/** — code restructuring
- **chore/** — maintenance, config, CI

### Commit Messages
Format: `<type>: <concise description>` — lowercase, imperative mood, no period.
- Types: `feat`, `fix`, `refactor`, `chore`, `debug`, `docs`, `test`
- First line ≤ 72 chars, describes the "what"
- Body (optional, blank line after subject): bullet points explaining "why" and key changes
- Use em dash (—) for inline separators, not hyphens
- Examples from this repo:
  - `feat: use console window handle for reliable terminal focus on notification click`
  - `fix: auto-uninstall hooks before app uninstall`
  - `refactor: align Settings API Console with onboarding multi-step pattern`
  - `chore: bump version to 2.2.1`

### PR Descriptions
```
## Summary
<1-3 bullet points: what changed and why>

## Test plan
- [ ] <Manual verification steps>
- [ ] <Edge cases checked>
```
- PR title follows the same `<type>: <description>` format as commits
- The `## Test plan` section is auto-stripped from GitHub Release notes by release.yml

## Documentation

- Design specs and implementation plans go in `docs/plans/` with naming format `YYYY-MM-DD-<topic>.md`
- Do NOT create docs in other locations (e.g., `docs/superpowers/`, `docs/specs/`)

## CI/CD

- **build.yml**: Push to main / PR → restore → build → test
- **release.yml**: Tag push `v*` → test → publish ClaudeTracker + HookBridge → bundle HookBridge into portable/installer → GitHub Release with PR-body notes (strips `## Test plan` section)

## Reference Project

The macOS reference implementation is at `D:\Projects\Claude-Usage-Tracker-main\` (Swift/SwiftUI).
