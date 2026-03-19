# Connection UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplify connection UX with silent OAuth token refresh, persistent WebView2 profile, and profile-aware connect flows.

**Architecture:** Add `TryRefreshTokenAsync()` to `ClaudeCodeSyncService` for silent OAuth renewal. Make `BrowserSignInWindow` persist its WebView2 user data folder. Differentiate the `PersonalUsageView` UI between default/single profile (unified Auto Detect) and additional profiles (explicit method buttons).

**Tech Stack:** .NET 8.0, WPF, WebView2, CommunityToolkit.Mvvm, xUnit + Moq

**Spec:** `docs/superpowers/specs/2026-03-19-connection-ux-redesign.md`

---

## File Structure

| File | Responsibility | Action |
|------|---------------|--------|
| `src/ClaudeTracker/Utilities/Constants.cs` | OAuth + WebView2 constants | Modify |
| `src/ClaudeTracker/Services/ClaudeCodeSyncService.cs` | Silent token refresh via refresh_token grant | Modify |
| `src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs` | Persistent WebView2 user data folder | Modify |
| `src/ClaudeTracker/ViewModels/PersonalUsageViewModel.cs` | Refresh-then-fallback in AutoDetect, profile-aware mode | Modify |
| `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml` | Profile-aware UI (unified vs explicit) | Modify |
| `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs` | Profile-aware button wiring, WebView2 auto-launch | Modify |
| `src/ClaudeTracker/Services/UsageRefreshCoordinator.cs` | Silent refresh before giving up (default profile) | Modify |
| `tests/ClaudeTracker.Tests/OAuthTokenRefreshTests.cs` | Unit tests for TryRefreshTokenAsync | Create |

**Note:** `ApiBillingViewModel` is listed in the spec but is NOT modified here. The API billing "Connect" logic lives entirely in `PersonalUsageView.xaml.cs` code-behind (Task 6), which calls `ApiBillingViewModel` commands directly. No ViewModel changes needed — this is intentional.

---

### Task 1: Add OAuth Constants

**Files:**
- Modify: `src/ClaudeTracker/Utilities/Constants.cs:9-17`

- [ ] **Step 1: Add OAuth and WebView2 constants**

In `Constants.cs`, add inside the `APIEndpoints` class (after line 16):

```csharp
public const string OAuthTokenEndpoint = "https://platform.claude.com/api/oauth/token";
public const string OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
```

Add a new nested class after `CredentialManager` (after line 80):

```csharp
public static class WebView2
{
    public static string ProfilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTracker", "WebView2Profile");
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/ClaudeTracker/Utilities/Constants.cs
git commit -m "feat: add OAuth token endpoint and WebView2 profile constants"
```

---

### Task 2: Implement Silent OAuth Token Refresh

**Files:**
- Modify: `src/ClaudeTracker/Services/ClaudeCodeSyncService.cs`
- Create: `tests/ClaudeTracker.Tests/OAuthTokenRefreshTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ClaudeTracker.Tests/OAuthTokenRefreshTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using Moq;
using Moq.Protected;

namespace ClaudeTracker.Tests;

public class OAuthTokenRefreshTests
{
    private readonly Mock<ICredentialService> _mockCredentials = new();

    private static string MakeCredentialsJson(string accessToken, string refreshToken, long expiresAt) =>
        JsonSerializer.Serialize(new CliCredentialsJson
        {
            ClaudeAiOauth = new CliOAuthData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt
            }
        });

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string responseBody)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseBody)
            });
        return new HttpClient(handler.Object);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_WithValidRefreshToken_ReturnsTrue()
    {
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var json = MakeCredentialsJson("old-token", "refresh-token-123", expiredAt);
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(json);

        var newExpiry = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 28800
        });
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, responseJson);

        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.True(result);
        _mockCredentials.Verify(c => c.WriteCliCredentials(It.Is<string>(s => s.Contains("new-access-token"))), Times.Once);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NoRefreshToken_ReturnsFalse()
    {
        var json = MakeCredentialsJson("token", "", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(json);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{}");
        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_ServerReturns401_ReturnsFalse()
    {
        var json = MakeCredentialsJson("old", "refresh-token", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(json);

        var httpClient = CreateMockHttpClient(HttpStatusCode.Unauthorized, "{}");
        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NoCredentialsFile_ReturnsFalse()
    {
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns((string?)null);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{}");
        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NetworkError_ReturnsFalse()
    {
        var json = MakeCredentialsJson("old", "refresh-token", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(json);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network unreachable"));
        var httpClient = new HttpClient(handler.Object);

        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
        _mockCredentials.Verify(c => c.WriteCliCredentials(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_ResponseMissingAccessToken_ReturnsFalse()
    {
        var json = MakeCredentialsJson("old", "refresh-token", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        _mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(json);

        var responseJson = JsonSerializer.Serialize(new { refresh_token = "new-refresh", expires_in = 28800 });
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, responseJson);

        var service = new ClaudeCodeSyncService(_mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
        _mockCredentials.Verify(c => c.WriteCliCredentials(It.IsAny<string>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudeTracker.Tests/ --filter "FullyQualifiedName~OAuthTokenRefresh" -v n`
Expected: FAIL — `ClaudeCodeSyncService` doesn't accept `HttpClient` and doesn't have `TryRefreshTokenAsync`

- [ ] **Step 3: Implement TryRefreshTokenAsync**

Modify `src/ClaudeTracker/Services/ClaudeCodeSyncService.cs`. Add `HttpClient` as an optional constructor parameter and add the refresh method:

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeCodeSyncService
{
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public ClaudeCodeSyncService(ICredentialService credentialService, HttpClient? httpClient = null)
    {
        _credentialService = credentialService;
        _httpClient = httpClient ?? new HttpClient();
    }

    // ... (keep all existing methods unchanged) ...

    /// <summary>
    /// Attempts to silently refresh an expired OAuth access token using the stored refresh token.
    /// On success, writes the updated credentials back to ~/.claude/.credentials.json.
    /// </summary>
    public async Task<bool> TryRefreshTokenAsync()
    {
        try
        {
            var json = ReadSystemCredentials();
            var parsed = ParseCredentials(json);
            var refreshToken = parsed?.ClaudeAiOauth?.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken))
            {
                LoggingService.Instance.Log("No refresh token available for silent refresh");
                return false;
            }

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = Constants.APIEndpoints.OAuthClientId
            });

            var response = await _httpClient.PostAsync(Constants.APIEndpoints.OAuthTokenEndpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.LogWarning($"OAuth token refresh failed: {response.StatusCode}");
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (!tokenResponse.TryGetProperty("access_token", out var newAccessToken))
                return false;

            // Update the credentials with new token data
            parsed!.ClaudeAiOauth!.AccessToken = newAccessToken.GetString();

            if (tokenResponse.TryGetProperty("refresh_token", out var newRefreshToken))
                parsed.ClaudeAiOauth.RefreshToken = newRefreshToken.GetString();

            if (tokenResponse.TryGetProperty("expires_in", out var expiresIn))
                parsed.ClaudeAiOauth.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn.GetInt64()).ToUnixTimeMilliseconds();

            var updatedJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
            _credentialService.WriteCliCredentials(updatedJson);

            LoggingService.Instance.Log("OAuth token refreshed silently");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Silent OAuth token refresh failed", ex);
            return false;
        }
    }
}
```

**Important**: The constructor changes from `(ICredentialService credentialService)` to `(ICredentialService credentialService, HttpClient? httpClient = null)`. The optional parameter preserves backward compatibility with DI.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudeTracker.Tests/ --filter "FullyQualifiedName~OAuthTokenRefresh" -v n`
Expected: All 6 tests PASS

- [ ] **Step 5: Run full test suite**

Run: `dotnet test tests/ClaudeTracker.Tests/ -v n`
Expected: All tests PASS (existing tests still work since `httpClient` parameter is optional)

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeTracker/Services/ClaudeCodeSyncService.cs tests/ClaudeTracker.Tests/OAuthTokenRefreshTests.cs
git commit -m "feat: add silent OAuth token refresh using stored refresh token"
```

---

### Task 3: Persistent WebView2 Profile

**Files:**
- Modify: `src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs`

- [ ] **Step 1: Set persistent UserDataFolder on WebView2**

In `BrowserSignInWindow.xaml.cs`, modify the `OnLoaded` method to use a persistent user data folder:

```csharp
private async void OnLoaded(object sender, RoutedEventArgs e)
{
    try
    {
        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Utilities.Constants.WebView2.ProfilePath);
        await WebView.EnsureCoreWebView2Async(env);
        WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        WebView.CoreWebView2.Navigate(_targetUrl);
    }
    catch (Exception ex)
    {
        LoggingService.Instance.LogError("WebView2 init failed", ex);
        StatusText.Text = "WebView2 initialization failed";
    }
}
```

The change: instead of `await WebView.EnsureCoreWebView2Async()` (default temp folder), we create a `CoreWebView2Environment` with a persistent `userDataFolder`. This means the user's Google OAuth session persists across app restarts — they sign in once, and subsequent opens auto-complete.

- [ ] **Step 2: Build and manually verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release`
Expected: Build succeeded

Manual test: Open Settings > Personal Usage > Open Browser for API section. Sign in with Google. Close window. Re-open — should not need to sign in again.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs
git commit -m "feat: persist WebView2 user data folder for one-time sign-in"
```

---

### Task 4: Update PersonalUsageViewModel with Refresh Fallback

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PersonalUsageViewModel.cs`

- [ ] **Step 1: Add IsDefaultProfile helper property**

Add a helper to determine if the active profile is the default (first/only) profile. Add this property after the existing `[ObservableProperty]` fields:

```csharp
/// <summary>True if the active profile is the first (default) profile or the only profile.</summary>
public bool IsDefaultProfile
{
    get
    {
        var profiles = _profileService.Profiles;
        if (profiles.Count <= 1) return true;
        var active = _profileService.ActiveProfile;
        return active != null && profiles.Count > 0 && profiles[0].Id == active.Id;
    }
}
```

- [ ] **Step 2: Update AutoDetect with silent refresh fallback**

Replace the `AutoDetect()` method body with the refresh-then-fallback chain:

```csharp
[RelayCommand]
private async Task AutoDetect()
{
    AutoDetectStatusText = "";
    AutoDetectSuccess = false;

    try
    {
        var (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();

        if (string.IsNullOrEmpty(token))
        {
            AutoDetectStatusText = "No Claude Code credentials found.\nMake sure Claude Code is installed and logged in.";
            return;
        }

        // If expired, try silent refresh before giving up
        if (isExpired)
        {
            var refreshed = await _cliSyncService.TryRefreshTokenAsync();
            if (refreshed)
            {
                // Re-read refreshed token info
                (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();
            }

            if (isExpired)
            {
                AutoDetectStatusText = "CLI token is expired and refresh failed.\nRun 'claude auth login' to re-authenticate.";
                return;
            }
        }

        var profile = _profileService.ActiveProfile;
        if (profile == null)
        {
            AutoDetectStatusText = "No active profile";
            return;
        }

        var success = _cliSyncService.SyncToProfile(_profileService, profile.Id);
        if (!success)
        {
            AutoDetectStatusText = "Failed to sync credentials";
            return;
        }

        try
        {
            await _apiService.FetchUsageData();
        }
        catch
        {
            // Token synced but usage fetch may need org ID setup — that's OK
        }

        AutoDetectSuccess = true;
        var planLabel = !string.IsNullOrEmpty(subType) ? $" ({subType})" : "";
        AutoDetectStatusText = $"Connected{planLabel}";
        IsConfigured = true;
        UpdateConnectedInfo();
    }
    catch (Exception ex)
    {
        AutoDetectStatusText = $"Error: {ex.Message}";
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PersonalUsageViewModel.cs
git commit -m "feat: add silent OAuth refresh fallback in AutoDetect flow"
```

---

### Task 5: Silent Refresh in UsageRefreshCoordinator (Default Profile Only)

**Files:**
- Modify: `src/ClaudeTracker/Services/UsageRefreshCoordinator.cs`

- [ ] **Step 1: Add ClaudeCodeSyncService dependency**

Add `ClaudeCodeSyncService` and `IProfileService` reference to the constructor. The coordinator already has `_profileService`. Add `_cliSyncService`:

In the constructor, add parameter `ClaudeCodeSyncService cliSyncService` and field `private readonly ClaudeCodeSyncService _cliSyncService;`.

- [ ] **Step 2: Add silent refresh before usage fetch**

In `RefreshAsync()`, inside the `if (profile.HasClaudeAI || profile.HasCliAccount)` block (around line 116-124), add a check before fetching usage. Insert this before `var usage = await _apiService.FetchUsageData();`:

```csharp
// Silent refresh for default profile if token expired
if (!string.IsNullOrEmpty(profile.CliCredentialsJSON) && _cliSyncService.IsTokenExpired(profile.CliCredentialsJSON))
{
    var isDefault = _profileService.Profiles.Count <= 1 || _profileService.Profiles[0].Id == profile.Id;
    if (isDefault)
    {
        var refreshed = await _cliSyncService.TryRefreshTokenAsync();
        if (refreshed)
        {
            _cliSyncService.SyncToProfile(_profileService, profile.Id);
            LoggingService.Instance.Log("Auto-refreshed expired OAuth token during usage poll");
        }
    }
}
```

- [ ] **Step 3: Verify DI auto-resolves (no code change needed)**

`ClaudeCodeSyncService` is already registered as `AddSingleton<ClaudeCodeSyncService>()` in `App.xaml.cs`. The DI container will auto-resolve it as a new constructor parameter of `UsageRefreshCoordinator`. No changes to `App.xaml.cs` are needed.

- [ ] **Step 4: Build and run full test suite**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release && dotnet test tests/ClaudeTracker.Tests/ -v n`
Expected: Build succeeded, all tests pass

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeTracker/Services/UsageRefreshCoordinator.cs
git commit -m "feat: silent OAuth refresh during usage poll for default profile"
```

---

### Task 6: Profile-Aware PersonalUsageView UI

**Files:**
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml`
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs`

This is the largest UI task. The default/single profile shows the unified "Auto Detect" button. Additional profiles show explicit "Connect via CLI" / "Connect via Browser" / "Manual paste" buttons.

- [ ] **Step 1: Update XAML — Section 1 (Subscription) setup panel**

Replace the `SetupPanel` StackPanel (lines 31-47) with profile-aware content:

```xml
<!-- Setup: Default/Single profile — unified Auto Detect -->
<StackPanel x:Name="SetupPanel">
    <Border x:Name="DefaultSetupCard" Style="{StaticResource UsageCardStyle}" Padding="16">
        <StackPanel>
            <TextBlock Text="Automatically syncs credentials from your Claude Code CLI login."
                       FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"
                       Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
            <StackPanel Orientation="Horizontal">
                <Button x:Name="AutoDetectButton" Content="Connect"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        Height="34" FontSize="12" />
                <TextBlock x:Name="AutoDetectStatus" FontSize="12" Margin="12,0,0,0"
                           VerticalAlignment="Center" TextWrapping="Wrap" MaxWidth="300" />
            </StackPanel>
        </StackPanel>
    </Border>

    <!-- Setup: Additional profile — explicit method choice -->
    <StackPanel x:Name="ExplicitSetupPanel" Visibility="Collapsed">
        <Border Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8">
            <StackPanel>
                <TextBlock Text="Connect via Claude Code CLI" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBlock Text="Reads OAuth credentials from ~/.claude/.credentials.json"
                           FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"
                           Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
                <StackPanel Orientation="Horizontal">
                    <Button x:Name="ExplicitCliButton" Content="Connect via CLI"
                            Style="{StaticResource MaterialDesignRaisedButton}"
                            Height="34" FontSize="12" />
                    <TextBlock x:Name="ExplicitCliStatus" FontSize="12" Margin="12,0,0,0"
                               VerticalAlignment="Center" TextWrapping="Wrap" MaxWidth="300" />
                </StackPanel>
            </StackPanel>
        </Border>

        <Border Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8">
            <StackPanel>
                <TextBlock Text="Connect via Session Key" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBlock TextWrapping="Wrap" FontSize="12" Margin="0,0,0,10"
                           Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}">
                    <Run Text="Paste your session key from claude.ai (F12 → Application → Cookies)" />
                </TextBlock>
                <TextBox x:Name="SessionKeyInput"
                         Style="{StaticResource MaterialDesignOutlinedTextBox}"
                         materialDesign:HintAssist.Hint="Session key..."
                         FontFamily="Consolas" FontSize="12" Margin="0,0,0,10" />
                <Button x:Name="SessionKeyTestButton" Content="Test Connection"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        Height="34" FontSize="12" HorizontalAlignment="Left" />
            </StackPanel>
        </Border>
    </StackPanel>
</StackPanel>
```

- [ ] **Step 2: Update XAML — Section 2 (API Billing) setup panel**

Replace the `ApiSetupPanel` (lines 86-135) with profile-aware content:

```xml
<StackPanel x:Name="ApiSetupPanel">
    <!-- Default profile: auto-launch WebView2 -->
    <Border x:Name="ApiAutoDetectCard" Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8">
        <StackPanel>
            <TextBlock Text="Sign in to API Console to track Claude Code spending."
                       FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"
                       Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
            <StackPanel Orientation="Horizontal">
                <Button x:Name="ApiAutoDetectButton" Content="Connect"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        Height="34" FontSize="12" />
                <TextBlock x:Name="ApiAutoDetectStatus" FontSize="12" Margin="12,0,0,0"
                           VerticalAlignment="Center" TextWrapping="Wrap" MaxWidth="300" />
            </StackPanel>
        </StackPanel>
    </Border>

    <!-- Additional profile: explicit options -->
    <StackPanel x:Name="ApiExplicitSetupPanel" Visibility="Collapsed">
        <Border x:Name="ApiBrowserCard" Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8"
                Visibility="Collapsed">
            <StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                    <materialDesign:PackIcon Kind="Web" Width="18" Height="18"
                                             Foreground="#2196F3" Margin="0,0,8,0" VerticalAlignment="Center" />
                    <TextBlock Text="Sign in with Browser" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center" />
                </StackPanel>
                <TextBlock Text="Sign in to API Console — session key is captured automatically"
                           FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"
                           Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
                <Button x:Name="ApiBrowserButton" Content="Open Browser"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        HorizontalAlignment="Left" Height="34" FontSize="12" />
            </StackPanel>
        </Border>

        <Border Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8">
            <StackPanel>
                <TextBlock Text="Manual Session Key" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBlock TextWrapping="Wrap" FontSize="12" Margin="0,0,0,12"
                           Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}">
                    <Run Text="1. Open " /><Run Text="platform.claude.com" FontWeight="SemiBold" />
                    <LineBreak />
                    <Run Text="2. Open DevTools (F12) → Application → Cookies" />
                    <LineBreak />
                    <Run Text="3. Copy the value of " /><Run Text="sessionKey" FontWeight="SemiBold" />
                </TextBlock>
                <TextBox x:Name="ApiKeyInput"
                         Style="{StaticResource MaterialDesignOutlinedTextBox}"
                         materialDesign:HintAssist.Hint="Session key..."
                         FontFamily="Consolas" FontSize="12" Margin="0,0,0,12" />
                <Button x:Name="ApiTestButton" Content="Test Connection"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        HorizontalAlignment="Left" Height="36" Margin="0,0,0,12" />
            </StackPanel>
        </Border>
    </StackPanel>

    <TextBlock x:Name="ApiStatusText" FontSize="12" TextWrapping="Wrap" Margin="0,0,0,12" />

    <StackPanel x:Name="ApiOrgPickerPanel" Visibility="Collapsed">
        <TextBlock Text="Select Organization" FontSize="12" FontWeight="Medium" Margin="0,0,0,6"
                   Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
        <ComboBox x:Name="ApiOrgCombo"
                  Style="{StaticResource MaterialDesignFilledComboBox}"
                  DisplayMemberPath="DisplayName" Margin="0,0,0,12" />
    </StackPanel>
</StackPanel>
```

- [ ] **Step 3: Update code-behind — profile-aware initialization**

In `PersonalUsageView.xaml.cs`, add profile detection and conditional UI setup. Add a helper method and update the constructor:

```csharp
private bool IsDefaultProfile()
{
    var profileService = App.Services.GetRequiredService<IProfileService>();
    var profiles = profileService.Profiles;
    if (profiles.Count <= 1) return true;
    var active = profileService.ActiveProfile;
    return active != null && profiles.Count > 0 && profiles[0].Id == active.Id;
}
```

In the constructor, after `DataContext = _vm;`, add:

```csharp
var isDefault = IsDefaultProfile();

// Section 1: Show unified or explicit setup
if (!isDefault)
{
    DefaultSetupCard.Visibility = Visibility.Collapsed;
    ExplicitSetupPanel.Visibility = Visibility.Visible;
}

// Section 2: Show unified or explicit setup
if (isDefault)
{
    ApiAutoDetectCard.Visibility = Visibility.Visible;
    ApiExplicitSetupPanel.Visibility = Visibility.Collapsed;
}
else
{
    ApiAutoDetectCard.Visibility = Visibility.Collapsed;
    ApiExplicitSetupPanel.Visibility = Visibility.Visible;
    if (BrowserSignInWindow.IsWebView2Available())
        ApiBrowserCard.Visibility = Visibility.Visible;
}
```

- [ ] **Step 4: Remove old handlers and wire up new buttons**

Remove the old `BrowserApiSignInButton.Click` handler (lines 40-59 of old code-behind) and the old `BrowserApiSignInCard` visibility check (line 37-38). These are replaced by the new profile-aware handlers below.

In the constructor, wire the new buttons:

```csharp
// Default profile: API section Auto Detect opens WebView2 directly
ApiAutoDetectButton.Click += async (_, _) =>
{
    if (!BrowserSignInWindow.IsWebView2Available())
    {
        ApiAutoDetectStatus.Text = "WebView2 not available. Use manual paste below.";
        ApiAutoDetectCard.Visibility = Visibility.Collapsed;
        ApiExplicitSetupPanel.Visibility = Visibility.Visible;
        return;
    }

    var window = new BrowserSignInWindow(Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
    window.Owner = Window.GetWindow(this);
    window.Show();
    var result = await window.ResultTask;
    if (result.HasValue)
    {
        _apiVm.ApiKey = result.Value.sessionKey;
        var profile = App.Services.GetRequiredService<IProfileService>().ActiveProfile;
        if (profile != null && result.Value.expiry.HasValue)
            profile.ApiSessionKeyExpiry = result.Value.expiry;

        ApiLoadingBar.Visibility = Visibility.Visible;
        await _apiVm.TestConnectionCommand.ExecuteAsync(null);
        ApiLoadingBar.Visibility = Visibility.Collapsed;
    }
    else
    {
        // User closed WebView2 without completing sign-in — show manual fallback
        ApiAutoDetectStatus.Text = "Sign-in cancelled. You can try again or use manual paste.";
        ApiAutoDetectCard.Visibility = Visibility.Collapsed;
        ApiExplicitSetupPanel.Visibility = Visibility.Visible;
        if (BrowserSignInWindow.IsWebView2Available())
            ApiBrowserCard.Visibility = Visibility.Visible;
    }
};

// Additional profile: explicit CLI button
ExplicitCliButton.Click += async (_, _) =>
{
    LoadingBar.Visibility = Visibility.Visible;
    await _vm.AutoDetectCommand.ExecuteAsync(null);
    LoadingBar.Visibility = Visibility.Collapsed;
    ExplicitCliStatus.Text = _vm.AutoDetectStatusText;
};

// Additional profile: explicit session key test
SessionKeyTestButton.Click += async (_, _) =>
{
    _vm.SessionKey = SessionKeyInput.Text;
    LoadingBar.Visibility = Visibility.Visible;
    await _vm.TestConnectionCommand.ExecuteAsync(null);
    LoadingBar.Visibility = Visibility.Collapsed;
};

// Additional profile: API browser sign-in
ApiBrowserButton.Click += async (_, _) =>
{
    var window = new BrowserSignInWindow(Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
    window.Owner = Window.GetWindow(this);
    window.Show();
    var result = await window.ResultTask;
    if (result.HasValue)
    {
        ApiKeyInput.Text = result.Value.sessionKey;
        _apiVm.ApiKey = result.Value.sessionKey;
        var profile = App.Services.GetRequiredService<IProfileService>().ActiveProfile;
        if (profile != null && result.Value.expiry.HasValue)
            profile.ApiSessionKeyExpiry = result.Value.expiry;

        ApiLoadingBar.Visibility = Visibility.Visible;
        await _apiVm.TestConnectionCommand.ExecuteAsync(null);
        ApiLoadingBar.Visibility = Visibility.Collapsed;
    }
};
```

- [ ] **Step 5: Build and manually test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release`
Expected: Build succeeded

Manual test checklist:
- With 1 profile: see unified "Connect" buttons for both sections
- Create a second profile, switch to it: see explicit CLI/Browser/Manual options
- Switch back to first profile: see unified buttons again

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs
git commit -m "feat: profile-aware connection UX — unified for default, explicit for additional"
```

---

### Task 7: Final Integration Test and Cleanup

**Files:**
- All modified files

- [ ] **Step 1: Full build**

Run: `dotnet build --configuration Release`
Expected: Build succeeded with no warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test -v n`
Expected: All tests pass

- [ ] **Step 3: Manual integration test**

Test the complete flow:
1. Default profile: Click "Connect" in Section 1 → should read CLI OAuth (+ refresh if expired)
2. Default profile: Click "Connect" in Section 2 → should open WebView2 to platform.claude.com
3. Close and reopen WebView2 → should not require re-login (persistent profile)
4. Create second profile → should show explicit CLI/Browser/Manual options
5. Disconnect → should clear all credentials correctly

- [ ] **Step 4: Commit any cleanup (if needed)**

Only commit if there are actual changes. Stage specific files, not `git add -A`:

```bash
git status
# Stage only the specific files that were modified
git commit -m "chore: integration test cleanup for connection UX redesign"
```
