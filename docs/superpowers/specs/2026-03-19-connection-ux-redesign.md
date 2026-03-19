# Connection UX Redesign

## Summary

Simplify the connection experience in Settings > Personal Usage by:
1. Adding silent OAuth token refresh using the stored refresh token
2. Using a persistent WebView2 profile so users only sign in once
3. Differentiating the UX between default/single profile (unified Auto Detect) and additional profiles (explicit method choice)

## Context

Currently, the Personal Usage settings page has two separate connection sections:
- **Claude.ai Subscription** (rate limit % tracking) — "Connect" button reads CLI OAuth from `~/.claude/.credentials.json`
- **Claude Code API Usage** ($ billing tracking) — "Open Browser" (WebView2) or manual sessionKey paste for `platform.claude.com`

Pain points:
- OAuth token expires (~8h) and the app has no way to renew it — user must re-run `claude auth login`
- WebView2 doesn't share the user's browser profile, so they must re-login with Google every time
- Two separate connect flows with different buttons is confusing
- Non-subscriber (API-key) users get blocked by `claude auth login` ("Pro/Max required")

## Design

### Section 1: Claude.ai Subscription (Rate Limit Tracking)

No UX changes. Behavior changes only:

**Silent OAuth refresh**: When the stored access token is expired but a refresh token exists, POST to the token endpoint before giving up:

```
POST https://platform.claude.com/api/oauth/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
refresh_token=<sk-ant-ort01-...>
client_id=9d1c250a-e61b-44d9-88ed-5944d1962f5e
```

On success: update `~/.claude/.credentials.json` with the new access token and expiry. On failure: fall through to existing error messages.

**Scope**: Silent refresh only runs automatically for the default profile (first/only profile). For additional profiles, it only runs when the user explicitly clicks Connect.

#### Default/Single Profile Flow

```
[Auto Detect] click
  -> Read ~/.claude/.credentials.json
  -> Token valid? -> Fetch usage -> Done
  -> Token expired + has refresh token? -> Silent refresh
    -> Success -> Update credentials -> Fetch usage -> Done
    -> Fail -> "Token expired. Run `claude auth login` in terminal, then click Connect again"
  -> No credentials? -> "No Claude Code credentials found. Log in with `claude auth login`"
```

#### Additional Profile Flow

Show separate, explicit options (not nested):

```
[Connect via CLI]      -> Reads ~/.claude/.credentials.json (+ refresh if expired)
[Connect via Browser]  -> WebView2 -> claude.ai session cookie capture
[Manual paste]         -> TextBox + "Test Connection" button
```

User chooses which method. No auto-detection cascade.

### Section 2: Claude Code API Usage (Billing Tracking)

#### Default/Single Profile Flow

```
[Auto Detect] click
  -> Try CLI OAuth first (same as Section 1 — silent refresh if needed)
  -> If CLI OAuth works -> use it for API billing too? (No — CLI OAuth is for personal usage, not API billing)
  -> Open WebView2 -> platform.claude.com (persistent profile)
  -> Capture sessionKey cookie -> org picker -> user picker -> Done
  -> WebView2 fails (error, user closes) -> Show manual fallback:
    "Open platform.claude.com -> F12 -> Application -> Cookies -> copy sessionKey"
    + paste textbox + Test Connection button
```

#### Additional Profile Flow

Same as default but without the nested cascade — show explicit options:

```
[Connect via Browser]  -> WebView2 -> platform.claude.com -> capture sessionKey
[Manual paste]         -> TextBox with instructions + "Test Connection"
```

### WebView2 Persistent Profile

Store the WebView2 user data folder at:
```
%APPDATA%\ClaudeTracker\WebView2Profile\
```

This persists the Google/Anthropic OAuth session across app restarts. The user authenticates with Google **once** in the WebView2; subsequent sign-ins auto-complete via cached session.

Changes to `BrowserSignInWindow`:
- Set `CoreWebView2CreationProperties.UserDataFolder` to the persistent path
- No other behavioral changes — cookie capture logic remains the same

### OAuth Token Refresh Implementation

New method in `ClaudeCodeSyncService`:

```csharp
public async Task<bool> TryRefreshToken()
```

- Reads refresh token from `~/.claude/.credentials.json`
- POSTs to `https://platform.claude.com/api/oauth/token` with `grant_type=refresh_token`
- On success: writes new access token + expiry back to credentials file
- On failure: returns false (caller handles fallback)

Integration points:
- `PersonalUsageViewModel.AutoDetect()` — call after detecting expired token
- `UsageRefreshCoordinator` — call before giving up on a failed usage fetch (default profile only)

### Constants

Add to `Constants.cs`:
```
OAuthTokenEndpoint = "https://platform.claude.com/api/oauth/token"
OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
WebView2ProfilePath = Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "ClaudeTracker", "WebView2Profile")
```

## Files to Modify

| File | Changes |
|------|---------|
| `Services/ClaudeCodeSyncService.cs` | Add `TryRefreshToken()`, write-back to credentials file |
| `Services/CredentialService.cs` | May need write method for updated tokens |
| `ViewModels/PersonalUsageViewModel.cs` | Update `AutoDetect()` with refresh + fallback chain |
| `ViewModels/ApiBillingViewModel.cs` | Update `TestConnection()` with WebView2 auto-launch for default profile |
| `Views/Settings/PersonalUsageView.xaml` | Profile-aware UI: unified vs explicit options |
| `Views/Settings/PersonalUsageView.xaml.cs` | WebView2 launch logic, profile-aware button wiring |
| `Views/BrowserSignInWindow.xaml.cs` | Persistent `UserDataFolder` for WebView2 |
| `Utilities/Constants.cs` | New OAuth + WebView2 constants |
| `Services/UsageRefreshCoordinator.cs` | Silent refresh before giving up (default profile only) |

## Out of Scope

- claude.ai session cookie capture (dropped — platform.claude.com is sufficient)
- Running `claude auth login` from the app (requires subscription, user can do it themselves)
- F12 console script (sessionKey is HttpOnly, not accessible via JS)
- Sharing Edge/Chrome browser profile (risk of corruption)

## Testing

- Unit test `TryRefreshToken()` with mock HTTP responses (success, 401, network error)
- Manual test: expire token by editing credentials file, verify silent refresh works
- Manual test: WebView2 persistent profile — sign in once, close app, reopen, verify no re-login needed
- Manual test: additional profile shows separate connection options
- Manual test: WebView2 closed by user shows manual fallback instructions
