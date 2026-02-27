# ClaudeTracker

Windows system tray application for real-time Claude AI usage monitoring. Port of the macOS Claude Usage Tracker.

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
├── ViewModels/      # MVVM ViewModels (CommunityToolkit.Mvvm)
├── Views/           # WPF XAML windows and user controls
│   ├── Controls/    # Reusable UI controls
│   └── Settings/    # Settings tab views (8 tabs)
├── TrayIcon/        # TrayIconManager + TrayIconRenderer (SkiaSharp)
├── Utilities/       # Constants, FormatterHelper, SessionKeyValidator, etc.
├── Themes/          # SharedStyles.xaml (Material Design)
├── Localization/    # Strings.resx (i18n)
└── Assets/          # app_icon.ico
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
| `NotificationService` | Usage alerts at 75%/90%/95% thresholds |
| `UsageRefreshCoordinator` | DispatcherTimer-based polling (default 30s, 10-300s range) |
| `ClaudeCodeSyncService` | CLI OAuth token sync from Windows Credential Manager |
| `AutoStartSessionService` | Auto-start session when usage resets to 0% |
| `TrayIconManager` | System tray lifecycle, popover positioning, context menu |
| `TrayIconRenderer` | SkiaSharp rendering for 5 icon styles (Battery, ProgressBar, Percentage, Ring, Compact) |

### Authentication (3-tier fallback in ClaudeApiService)

1. Claude.ai session key (cookie-based)
2. CLI OAuth token (Bearer, from saved profile or `~/.claude/.credentials.json`)
3. API Console session key (billing only)

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
```

Note: Debug build will fail if the app is running (file lock). Use Release config or close the app first.

## Key File Paths (Runtime)

- Settings: `%APPDATA%\ClaudeTracker\settings.json`
- Logs: `%APPDATA%\ClaudeTracker\logs\claudetracker-.log`
- CLI Credentials: `%USERPROFILE%\.claude\.credentials.json`

## Conventions

- All ViewModels use `[ObservableProperty]` and `[RelayCommand]` attributes from CommunityToolkit.Mvvm
- Service interfaces live in `Services/Interfaces/` — always program to the interface
- Settings are camelCase JSON with thread-safe locking in SettingsService
- Tray icon rendering is DPI-aware (Win32 `GetDpiForSystem`)
- Usage status levels: Safe (green, >20% remaining), Moderate (orange, 10-20%), Critical (red, <10%)
- Constants centralized in `Utilities/Constants.cs`

## CI/CD

- **build.yml**: Push to main / PR → restore → build → test
- **release.yml**: Tag push `v*` → test → publish (self-contained + framework-dependent) → GitHub Release

## Reference Project

The macOS reference implementation is at `D:\Projects\Claude-Usage-Tracker-main\` (Swift/SwiftUI).
