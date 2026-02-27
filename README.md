# ClaudeTracker

A Windows system tray application that monitors your Claude AI usage limits in real-time. Get instant visibility into your session and weekly usage, overage costs, and receive alerts before hitting rate limits.

Windows port of [Claude Usage Tracker](https://github.com/hamed-elfayome/Claude-Usage-Tracker) (macOS).

## Features

- **Real-time usage monitoring** — Session (5-hour) and weekly usage percentages, Opus/Sonnet breakdowns
- **5 tray icon styles** — Battery, Progress Bar, Percentage, Ring, Compact (Dot)
- **Custom icon colors** — Status-based (green/orange/red), monochrome, or pick any color
- **Multi-profile support** — Manage multiple Claude accounts with isolated credentials
- **Notifications** — Configurable alerts at 75%, 90%, 95% usage thresholds
- **API billing tracking** — Monitor console spend and prepaid credits
- **Claude Code CLI sync** — Automatically reads OAuth tokens from Windows Credential Manager
- **Auto-start sessions** — Detects 0% usage reset and starts a new session automatically
- **Dark/Light theme** — Follows system theme or manual override
- **DPI-aware** — Crisp icons at any display scaling (100%–200%)

## Screenshots

| Tray Icon | Popover Dashboard | Settings |
|-----------|-------------------|----------|
| System tray with live usage | Click tray icon for full dashboard | Configure accounts, appearance, alerts |

## Installation

### From Release

1. Download the latest `.zip` from [Releases](https://github.com/hamed-elfayome/ClaudeTracker/releases)
2. Extract to any folder
3. Run `ClaudeTracker.exe`

### Build from Source

**Requirements:**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10 (build 17763) or later

```bash
git clone https://github.com/hamed-elfayome/ClaudeTracker.git
cd ClaudeTracker
dotnet build
dotnet run --project src/ClaudeTracker
```

## Setup

1. Launch ClaudeTracker — it appears in the system tray
2. Right-click the tray icon > **Settings**
3. Go to **Connect** tab
4. Enter your Claude session key (`sk-ant-...`) and click **Test Connection**
5. Select your organization and save

### Getting your session key

1. Go to [claude.ai](https://claude.ai) and log in
2. Open browser DevTools (`F12`) > **Application** > **Cookies**
3. Copy the value of `sessionKey`

### Claude Code CLI sync

If you have [Claude Code](https://docs.anthropic.com/en/docs/claude-code) installed, ClaudeTracker can automatically read its OAuth token from Windows Credential Manager — no manual key entry needed.

## Tech Stack

- **.NET 8** / C# / WPF
- [MaterialDesignInXaml](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) — Material Design UI
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — System tray integration
- [SkiaSharp](https://github.com/mono/SkiaSharp) — DPI-aware tray icon rendering
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM pattern
- Microsoft.Extensions.DependencyInjection — DI container

## Project Structure

```
src/ClaudeTracker/
├── Models/          Data models (usage, profiles, settings)
├── Services/        API client, credentials, notifications, refresh
├── ViewModels/      MVVM view models
├── Views/           WPF windows and user controls
├── TrayIcon/        Tray icon management and SkiaSharp rendering
├── Utilities/       Constants, validators, formatters
├── Themes/          Shared WPF styles
└── Localization/    Resource strings
```

## Configuration

Settings are stored in `%APPDATA%\ClaudeTracker\settings.json`. Logs are written to `%APPDATA%\ClaudeTracker\logs\`.

| Setting | Default | Description |
|---------|---------|-------------|
| Refresh interval | 30s | How often usage is fetched (10–300s) |
| Icon style | Battery | Tray icon visualization style |
| Theme | System | Auto, Light, or Dark |
| Notifications | Enabled | Alert at 75%, 90%, 95% |
| Launch at login | Off | Start with Windows via Registry |
| Auto-start session | Off | Auto-start when usage resets to 0% |

## License

MIT
