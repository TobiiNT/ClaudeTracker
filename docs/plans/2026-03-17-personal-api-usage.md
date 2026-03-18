# Personal Claude Code API Usage Integration

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace org-level API billing display with personal Claude Code usage (cost, sessions, lines accepted) using the platform.claude.com internal analytics API.

**Architecture:** After the user connects their API session key and selects an org (existing flow in `ApiBillingViewModel`), automatically fetch the full user list from `/api/claude_code/metrics_aggs/users` and present a dropdown for the user to select their identity (email or API key name). Store the selected search term in `Profile.ApiUserSearch`. On each refresh cycle, call `/api/claude_code/metrics_aggs/users?search=` to get personal metrics. The popover UI shows a "My Claude Code" card with personal cost/sessions/lines.

**Tech Stack:** .NET 8, WPF, System.Text.Json, xUnit + Moq

---

## Discovered API Endpoints

These internal platform.claude.com endpoints were verified working with session cookie auth:

| Endpoint | Path | Auth | Returns |
|----------|------|------|---------|
| Claude Code User Metrics | `GET /api/claude_code/metrics_aggs/users?search={email}&organization_uuid={uuid}&start_date=YYYY-MM-DD&end_date=YYYY-MM-DD&limit=1&offset=0&sort_by=total_cost_usd&sort_order=desc` | Cookie: `sessionKey=...` | Per-user cost, lines, sessions |
| Claude Code Overview | `GET /api/claude_code/metrics_aggs/overview?organization_uuid={uuid}&start_date=YYYY-MM-DD&end_date=YYYY-MM-DD` | Cookie: `sessionKey=...` | Org summary with total_cost, active_users |
| API Keys | `GET /api/console/organizations/{uuid}/api_keys` | Cookie: `sessionKey=...` | All API keys with id, name, status |
| Usage Cost (per-key) | `GET /api/organizations/{uuid}/workspaces/default/usage_cost?starting_on=YYYY-MM-DD&ending_before=YYYY-MM-DD&group_by=api_key_id` | Cookie: `sessionKey=...` | Per-key cost breakdown by date/model |

**Primary endpoint for this feature:** Claude Code User Metrics (`/api/claude_code/metrics_aggs/users?search=`)

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/ClaudeTracker/Models/APIUsage.cs` | Modify | Add `ClaudeCodeUserMetrics` model + extend `APIUsage` with personal fields |
| `src/ClaudeTracker/Models/Profile.cs` | Modify | Add `ApiUserSearch` field (selected identity from picker) |
| `src/ClaudeTracker/Services/Interfaces/IClaudeApiService.cs` | Modify | Add `FetchClaudeCodeUserMetrics` + `FetchClaudeCodeAllUsers` methods |
| `src/ClaudeTracker/Services/ClaudeApiService.cs` | Modify | Implement both methods |
| `src/ClaudeTracker/Services/UsageRefreshCoordinator.cs` | Modify | Call personal metrics in refresh cycle |
| `src/ClaudeTracker/ViewModels/PopoverViewModel.cs` | Modify | Add personal usage properties |
| `src/ClaudeTracker/Views/PopoverWindow.xaml` | Modify | Update API card to show personal data |
| `src/ClaudeTracker/Views/PopoverWindow.xaml.cs` | Modify | Bind personal usage fields |
| `src/ClaudeTracker/ViewModels/ApiBillingViewModel.cs` | Modify | Add user picker after org selection |
| `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml` | Modify | Add user picker dropdown |
| `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs` | Modify | Wire user picker |
| `src/ClaudeTracker/Utilities/Constants.cs` | Modify | Add ClaudeCode API base path |
| `tests/ClaudeTracker.Tests/ClaudeCodeMetricsTests.cs` | Create | Unit tests for parsing + model |

---

### Task 1: Add ClaudeCodeUserMetrics Model

**Files:**
- Modify: `src/ClaudeTracker/Models/APIUsage.cs`
- Create: `tests/ClaudeTracker.Tests/ClaudeCodeMetricsTests.cs`

- [ ] **Step 1: Write the test for JSON deserialization**

```csharp
// tests/ClaudeTracker.Tests/ClaudeCodeMetricsTests.cs
using System.Text.Json;
using ClaudeTracker.Models;

namespace ClaudeTracker.Tests;

public class ClaudeCodeMetricsTests
{
    [Fact]
    public void ClaudeCodeUserMetrics_Deserialize_FromApiResponse()
    {
        var json = """
        {
            "email": "user@example.com",
            "api_key_name": null,
            "status": "active",
            "avg_cost_per_day": "80.93",
            "avg_lines_accepted_per_day": 1423,
            "total_cost": "971.18",
            "total_lines_accepted": 17076,
            "total_sessions": 51,
            "last_active": "2026-03-17T00:00:00",
            "prs_with_cc": 3,
            "total_prs": 10,
            "prs_with_cc_percentage": 30.0
        }
        """;

        var metrics = JsonSerializer.Deserialize<ClaudeCodeUserMetrics>(json);

        Assert.NotNull(metrics);
        Assert.Equal("user@example.com", metrics.Email);
        Assert.Equal(971.18, metrics.TotalCostUsd, 2);
        Assert.Equal(17076, metrics.TotalLinesAccepted);
        Assert.Equal(51, metrics.TotalSessions);
        Assert.Equal(80.93, metrics.AvgCostPerDayUsd, 2);
        Assert.Equal("$971.18", metrics.FormattedTotalCost);
        Assert.Equal("$80.93/day", metrics.FormattedAvgCostPerDay);
    }

    [Fact]
    public void ClaudeCodeUserMetrics_Deserialize_ApiKeyUser()
    {
        var json = """
        {
            "email": null,
            "api_key_name": "my-claude-key",
            "status": "active",
            "avg_cost_per_day": "5.50",
            "avg_lines_accepted_per_day": 200,
            "total_cost": "44.00",
            "total_lines_accepted": 1600,
            "total_sessions": 10,
            "last_active": "2026-03-17T00:00:00",
            "prs_with_cc": 0,
            "total_prs": 0,
            "prs_with_cc_percentage": 0
        }
        """;

        var metrics = JsonSerializer.Deserialize<ClaudeCodeUserMetrics>(json);

        Assert.NotNull(metrics);
        Assert.Equal("my-claude-key", metrics.DisplayName);
    }

    [Fact]
    public void ClaudeCodeMetricsResponse_Deserialize_WithPagination()
    {
        var json = """
        {
            "organization_id": "org-123",
            "start_date": "2026-03-01",
            "end_date": "2026-04-01",
            "total_users": 1,
            "users": [{
                "email": "user@example.com",
                "api_key_name": null,
                "status": "active",
                "avg_cost_per_day": "10.00",
                "avg_lines_accepted_per_day": 500,
                "total_cost": "100.00",
                "total_lines_accepted": 5000,
                "total_sessions": 20,
                "last_active": "2026-03-17T00:00:00",
                "prs_with_cc": 0,
                "total_prs": 0,
                "prs_with_cc_percentage": 0
            }],
            "pagination": { "limit": 1, "offset": 0, "total": 1, "has_next": false }
        }
        """;

        var response = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

        Assert.NotNull(response);
        Assert.Single(response.Users);
        Assert.Equal(1, response.TotalUsers);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "ClaudeCodeMetricsTests" --configuration Release`
Expected: FAIL — `ClaudeCodeUserMetrics` type does not exist

- [ ] **Step 3: Write the models**

```csharp
// Add to src/ClaudeTracker/Models/APIUsage.cs

/// <summary>Per-user Claude Code metrics from platform.claude.com analytics API.</summary>
public class ClaudeCodeUserMetrics
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("api_key_name")]
    public string? ApiKeyName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("avg_cost_per_day")]
    public string AvgCostPerDay { get; set; } = "0";

    [JsonPropertyName("avg_lines_accepted_per_day")]
    public int AvgLinesAcceptedPerDay { get; set; }

    [JsonPropertyName("total_cost")]
    public string TotalCost { get; set; } = "0";

    [JsonPropertyName("total_lines_accepted")]
    public int TotalLinesAccepted { get; set; }

    [JsonPropertyName("total_sessions")]
    public int TotalSessions { get; set; }

    [JsonPropertyName("last_active")]
    public string? LastActive { get; set; }

    [JsonPropertyName("prs_with_cc")]
    public int PrsWithCc { get; set; }

    [JsonPropertyName("total_prs")]
    public int TotalPrs { get; set; }

    [JsonPropertyName("prs_with_cc_percentage")]
    public double PrsWithCcPercentage { get; set; }

    // Computed
    [JsonIgnore]
    public double TotalCostUsd => double.TryParse(TotalCost, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    [JsonIgnore]
    public double AvgCostPerDayUsd => double.TryParse(AvgCostPerDay, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    [JsonIgnore]
    public string DisplayName => Email ?? ApiKeyName ?? "Unknown";

    [JsonIgnore]
    public string FormattedTotalCost => $"${TotalCostUsd:F2}";

    [JsonIgnore]
    public string FormattedAvgCostPerDay => $"${AvgCostPerDayUsd:F2}/day";
}

/// <summary>Response wrapper for Claude Code user metrics API.</summary>
public class ClaudeCodeMetricsResponse
{
    [JsonPropertyName("organization_id")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("total_users")]
    public int TotalUsers { get; set; }

    [JsonPropertyName("users")]
    public List<ClaudeCodeUserMetrics> Users { get; set; } = new();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "ClaudeCodeMetricsTests" --configuration Release`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeTracker/Models/APIUsage.cs tests/ClaudeTracker.Tests/ClaudeCodeMetricsTests.cs
git commit -m "feat: add ClaudeCodeUserMetrics model for personal usage tracking"
```

---

### Task 2: Add API User Identity to Profile

**Files:**
- Modify: `src/ClaudeTracker/Models/Profile.cs`

- [ ] **Step 1: Add `ApiUserSearch` property to Profile**

Add after `ApiOrganizationId` (line 26). This stores the user's selected identity (email or API key name) from the picker:

```csharp
[JsonPropertyName("apiUserSearch")]
public string? ApiUserSearch { get; set; }
```

- [ ] **Step 2: Add `PersonalMetrics` property to Profile**

Add after `ApiUsage` (line 49):

```csharp
[JsonPropertyName("personalMetrics")]
public ClaudeCodeUserMetrics? PersonalMetrics { get; set; }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeTracker/Models/Profile.cs
git commit -m "feat: add ApiUserSearch and PersonalMetrics to Profile"
```

---

### Task 3: Add ClaudeCode Metrics Endpoint Constant

**Files:**
- Modify: `src/ClaudeTracker/Utilities/Constants.cs`

- [ ] **Step 1: Add ClaudeCode API path constant**

Add to `APIEndpoints` class (after line 13):

```csharp
public const string ClaudeCodeMetrics = "https://platform.claude.com/api/claude_code/metrics_aggs";
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/ClaudeTracker/Utilities/Constants.cs
git commit -m "feat: add ClaudeCode metrics API endpoint constant"
```

---

### Task 4: Implement ClaudeCode Metrics API Methods

**Files:**
- Modify: `src/ClaudeTracker/Services/Interfaces/IClaudeApiService.cs`
- Modify: `src/ClaudeTracker/Services/ClaudeApiService.cs`

- [ ] **Step 1: Add both methods to interface**

Add to `IClaudeApiService` (after line 24):

```csharp
/// <summary>Fetches all Claude Code users for the organization (for identity picker).</summary>
Task<List<ClaudeCodeUserMetrics>> FetchClaudeCodeAllUsers(string organizationUuid, string apiSessionKey);
/// <summary>Fetches personal Claude Code usage metrics for a specific user.</summary>
Task<ClaudeCodeUserMetrics?> FetchClaudeCodeUserMetrics(string organizationUuid, string apiSessionKey, string search);
```

- [ ] **Step 2: Implement both methods in ClaudeApiService**

Add before the `PerformClaudeRequest` method (before line 372). Note: `UrlBuilder` does NOT have query parameter support — use string interpolation with `Uri.EscapeDataString`:

```csharp
public async Task<List<ClaudeCodeUserMetrics>> FetchClaudeCodeAllUsers(
    string organizationUuid, string apiSessionKey)
{
    var now = DateTime.UtcNow;
    var startOfMonth = new DateTime(now.Year, now.Month, 1);
    var startOfNextMonth = startOfMonth.AddMonths(1);

    var baseUrl = Constants.APIEndpoints.ClaudeCodeMetrics;
    var url = new Uri($"{baseUrl}/users?organization_uuid={Uri.EscapeDataString(organizationUuid)}" +
        $"&start_date={startOfMonth:yyyy-MM-dd}&end_date={startOfNextMonth:yyyy-MM-dd}" +
        $"&limit=200&offset=0&sort_by=total_cost_usd&sort_order=desc");

    var client = _httpClientFactory.CreateClient("Claude");
    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
    request.Headers.Add("Accept", "application/json");

    var response = await client.SendAsync(request);
    await EnsureSuccessResponse(response, "claude_code/metrics_aggs/users (all)");

    var json = await response.Content.ReadAsStringAsync();
    var metricsResponse = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

    return metricsResponse?.Users ?? new List<ClaudeCodeUserMetrics>();
}

public async Task<ClaudeCodeUserMetrics?> FetchClaudeCodeUserMetrics(
    string organizationUuid, string apiSessionKey, string search)
{
    var now = DateTime.UtcNow;
    var startOfMonth = new DateTime(now.Year, now.Month, 1);
    var startOfNextMonth = startOfMonth.AddMonths(1);

    var baseUrl = Constants.APIEndpoints.ClaudeCodeMetrics;
    var url = new Uri($"{baseUrl}/users?organization_uuid={Uri.EscapeDataString(organizationUuid)}" +
        $"&start_date={startOfMonth:yyyy-MM-dd}&end_date={startOfNextMonth:yyyy-MM-dd}" +
        $"&search={Uri.EscapeDataString(search)}&limit=1&offset=0" +
        $"&sort_by=total_cost_usd&sort_order=desc");

    var client = _httpClientFactory.CreateClient("Claude");
    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("Cookie", $"sessionKey={apiSessionKey}");
    request.Headers.Add("Accept", "application/json");

    var response = await client.SendAsync(request);
    await EnsureSuccessResponse(response, "claude_code/metrics_aggs/users");

    var json = await response.Content.ReadAsStringAsync();
    var metricsResponse = JsonSerializer.Deserialize<ClaudeCodeMetricsResponse>(json);

    return metricsResponse?.Users.FirstOrDefault();
}
```

- [ ] **Step 3: Add stubs to MockClaudeApiService**

Check `src/ClaudeTracker/Services/MockClaudeApiService.cs` and add:

```csharp
public Task<List<ClaudeCodeUserMetrics>> FetchClaudeCodeAllUsers(
    string organizationUuid, string apiSessionKey)
    => Task.FromResult(new List<ClaudeCodeUserMetrics>());

public Task<ClaudeCodeUserMetrics?> FetchClaudeCodeUserMetrics(
    string organizationUuid, string apiSessionKey, string search)
    => Task.FromResult<ClaudeCodeUserMetrics?>(null);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeTracker/Services/Interfaces/IClaudeApiService.cs src/ClaudeTracker/Services/ClaudeApiService.cs src/ClaudeTracker/Services/MockClaudeApiService.cs
git commit -m "feat: implement FetchClaudeCodeUserMetrics API call"
```

---

### Task 5: Wire Into UsageRefreshCoordinator

**Files:**
- Modify: `src/ClaudeTracker/Services/UsageRefreshCoordinator.cs`
- Modify: `src/ClaudeTracker/Services/Interfaces/IProfileService.cs` (if needed)

- [ ] **Step 1: Add personal metrics fetch to RefreshAsync**

Add as a **sibling block** after the `if (profile.HasAPIConsole)` block (after line 134, after the closing `}`), NOT inside it — personal metrics need the API session key but are a separate concern:

```csharp
// Fetch personal Claude Code metrics (non-fatal)
if (profile.HasAPIConsole && !string.IsNullOrEmpty(profile.ApiUserSearch))
{
    try
    {
        var personalMetrics = await _apiService.FetchClaudeCodeUserMetrics(
            profile.ApiOrganizationId!, profile.ApiSessionKey!, profile.ApiUserSearch);
        _profileService.UpdatePersonalMetrics(profile.Id, personalMetrics);
    }
    catch (HttpRequestException ex)
    {
        LoggingService.Instance.LogWarning($"Personal metrics fetch failed (non-fatal): {ex.Message}");
    }
}
```

- [ ] **Step 2: Add UpdatePersonalMetrics to IProfileService and ProfileService**

Check the existing `UpdateUsageData` method signature and add a similar one:

```csharp
// In IProfileService interface (src/ClaudeTracker/Services/Interfaces/IProfileService.cs)
void UpdatePersonalMetrics(Guid profileId, ClaudeCodeUserMetrics? metrics);

// In ProfileService implementation (src/ClaudeTracker/Services/ProfileService.cs)
// Follow same pattern as UpdateUsageData (line 127): use _settingsService.Settings and _settingsService.Save()
public void UpdatePersonalMetrics(Guid profileId, ClaudeCodeUserMetrics? metrics)
{
    var profile = _settingsService.Settings.Profiles.FirstOrDefault(p => p.Id == profileId);
    if (profile == null) return;
    profile.PersonalMetrics = metrics;
    _settingsService.Save();
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build --configuration Release && dotnet test --configuration Release`
Expected: Build succeeded, all tests pass

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeTracker/Services/UsageRefreshCoordinator.cs src/ClaudeTracker/Services/Interfaces/IProfileService.cs src/ClaudeTracker/Services/ProfileService.cs
git commit -m "feat: fetch personal Claude Code metrics in refresh cycle"
```

---

### Task 6: Update Popover ViewModel with Personal Metrics

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`

- [ ] **Step 1: Add personal metrics properties**

Add new `[ObservableProperty]` fields alongside the existing API usage ones:

```csharp
[ObservableProperty] private bool _hasPersonalMetrics;
[ObservableProperty] private string _personalCostText = "";
[ObservableProperty] private string _personalAvgCostText = "";
[ObservableProperty] private string _personalSessionsText = "";
[ObservableProperty] private string _personalLinesText = "";
```

- [ ] **Step 2: Populate from profile in UpdateUsageDisplay**

Add after the existing `HasApiUsage` block (after line 289):

```csharp
var personalMetrics = profile.PersonalMetrics;
HasPersonalMetrics = personalMetrics != null;
if (personalMetrics != null)
{
    PersonalCostText = personalMetrics.FormattedTotalCost;
    PersonalAvgCostText = personalMetrics.FormattedAvgCostPerDay;
    PersonalSessionsText = $"{personalMetrics.TotalSessions} sessions";
    PersonalLinesText = $"{personalMetrics.TotalLinesAccepted:N0} lines";
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PopoverViewModel.cs
git commit -m "feat: add personal metrics properties to PopoverViewModel"
```

---

### Task 7: Update Popover UI to Show Personal Usage

**Files:**
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml.cs`

- [ ] **Step 1: Add personal usage card to PopoverWindow.xaml**

Add after the API Credits card (after line 211), before the Sessions card:

```xml
<!-- ── Personal Claude Code Usage ── -->
<Border x:Name="PersonalCard" Style="{StaticResource UsageCardStyle}" Visibility="Collapsed">
    <StackPanel>
        <Grid Margin="0,0,0,6">
            <StackPanel Orientation="Horizontal">
                <materialDesign:PackIcon Kind="AccountCircle" Width="15" Height="15"
                                         Foreground="#888" VerticalAlignment="Center"
                                         Margin="0,0,5,0" />
                <TextBlock Text="My Claude Code" FontSize="13" FontWeight="Medium" Foreground="#666" />
            </StackPanel>
            <TextBlock x:Name="PersonalCostText" HorizontalAlignment="Right"
                       FontSize="17" FontWeight="SemiBold" Foreground="#444" />
        </Grid>
        <Grid Margin="0,2,0,0">
            <TextBlock x:Name="PersonalAvgCostText" FontSize="12" Foreground="#999" />
            <TextBlock x:Name="PersonalSessionsText" HorizontalAlignment="Center"
                       FontSize="12" Foreground="#999" />
            <TextBlock x:Name="PersonalLinesText" HorizontalAlignment="Right"
                       FontSize="12" Foreground="#999" />
        </Grid>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Bind personal data in PopoverWindow.xaml.cs**

Add after the API card binding (after line 184):

```csharp
// Personal Claude Code metrics
PersonalCard.Visibility = _viewModel.HasPersonalMetrics ? Visibility.Visible : Visibility.Collapsed;
if (_viewModel.HasPersonalMetrics)
{
    PersonalCostText.Text = _viewModel.PersonalCostText;
    PersonalAvgCostText.Text = _viewModel.PersonalAvgCostText;
    PersonalSessionsText.Text = _viewModel.PersonalSessionsText;
    PersonalLinesText.Text = _viewModel.PersonalLinesText;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/ClaudeTracker/Views/PopoverWindow.xaml src/ClaudeTracker/Views/PopoverWindow.xaml.cs
git commit -m "feat: add personal Claude Code usage card to popover"
```

---

### Task 8: Add User Identity Picker in API Settings

After the user connects their API session key and selects an org, automatically fetch the Claude Code users list and show a ComboBox for them to pick their identity. This replaces manual email input.

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/ApiBillingViewModel.cs` — add user picker logic
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml` — add user ComboBox
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs` — wire user picker

- [ ] **Step 1: Add user picker properties + auto-fetch to ApiBillingViewModel**

Add to `ApiBillingViewModel`:

```csharp
// New fields
private readonly ISettingsService _settingsService;
[ObservableProperty] private bool _showUserPicker;
[ObservableProperty] private ClaudeCodeUserMetrics? _selectedUser;
public ObservableCollection<ClaudeCodeUserMetrics> ClaudeCodeUsers { get; } = new();

// Update constructor to inject ISettingsService:
public ApiBillingViewModel(IClaudeApiService apiService, IProfileService profileService, ISettingsService settingsService)
{
    _apiService = apiService;
    _profileService = profileService;
    _settingsService = settingsService;
    // ... existing init
}
```

At the end of `SaveConfiguration()`, after `ShowOrgPicker = false;`, add:

```csharp
// Auto-fetch Claude Code users for identity picker
try
{
    var users = await _apiService.FetchClaudeCodeAllUsers(SelectedOrg.Id, ApiKey.Trim());
    ClaudeCodeUsers.Clear();
    foreach (var user in users)
        ClaudeCodeUsers.Add(user);

    if (users.Count > 0)
    {
        ShowUserPicker = true;
        var profile = _profileService.ActiveProfile;
        if (!string.IsNullOrEmpty(profile?.ApiUserSearch))
            SelectedUser = users.FirstOrDefault(u => u.DisplayName == profile.ApiUserSearch);
    }
}
catch (Exception ex)
{
    LoggingService.Instance.LogWarning($"Failed to fetch Claude Code users: {ex.Message}");
}
```

- [ ] **Step 2: Add SaveUserSelection command**

```csharp
[RelayCommand]
private void SaveUserSelection()
{
    if (SelectedUser == null) return;
    var profile = _profileService.ActiveProfile;
    if (profile == null) return;

    profile.ApiUserSearch = SelectedUser.DisplayName;
    _settingsService.Save();

    ShowUserPicker = false;
    TestStatus = $"Tracking: {SelectedUser.DisplayName}";
    TestSuccess = true;
}
```

- [ ] **Step 3: Add user picker UI to PersonalUsageView.xaml**

Add after `ApiOrgPickerPanel` (after line 205), inside `ApiSetupPanel`:

```xml
<StackPanel x:Name="ApiUserPickerPanel" Visibility="Collapsed" Margin="0,8,0,0">
    <TextBlock Text="Select your Claude Code identity" FontSize="12" FontWeight="Medium" Margin="0,0,0,6"
               Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
    <ComboBox x:Name="ApiUserCombo"
              Style="{StaticResource MaterialDesignFilledComboBox}"
              DisplayMemberPath="DisplayName"
              Margin="0,0,0,8" />
    <Button x:Name="ApiUserSaveButton" Content="Confirm"
            Style="{StaticResource MaterialDesignRaisedButton}"
            HorizontalAlignment="Left" Height="36" />
</StackPanel>
```

- [ ] **Step 4: Wire user picker in PersonalUsageView.xaml.cs**

Add after `ApiOrgCombo.ItemsSource = _apiVm.Organizations;` (line 116):

```csharp
ApiUserCombo.ItemsSource = _apiVm.ClaudeCodeUsers;

ApiUserSaveButton.Click += (_, _) =>
{
    _apiVm.SelectedUser = ApiUserCombo.SelectedItem as ClaudeCodeUserMetrics;
    _apiVm.SaveUserSelectionCommand.Execute(null);
};
```

In `UpdateApiUI()`, add after existing lines:

```csharp
ApiUserPickerPanel.Visibility = _apiVm.ShowUserPicker ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 5: Build and test**

Run: `dotnet build --configuration Release`
Expected: Build succeeded

Manual test: enter session key → select org → user picker appears → select identity → saved

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeTracker/ViewModels/ApiBillingViewModel.cs src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs
git commit -m "feat: add Claude Code user identity picker in API settings"
```

---

### Task 9: Run Full Test Suite and Integration Smoke Test

- [ ] **Step 1: Run all tests**

Run: `dotnet test --configuration Release`
Expected: All tests pass (including new ClaudeCodeMetricsTests)

- [ ] **Step 2: Manual smoke test**

1. Launch ClaudeTracker
2. Go to Settings → Personal Usage → API section
3. Enter session key → Test Connection → Select org
4. User identity picker appears → Select your name/email
5. Wait for next refresh cycle
6. Verify popover shows "My Claude Code" card with personal cost/sessions/lines

- [ ] **Step 3: Verify logs**

Check `%APPDATA%\ClaudeTracker\logs\` for:
- Successful `claude_code/metrics_aggs/users` fetch
- No errors from the new endpoint

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: personal Claude Code usage tracking via platform analytics API"
```

---

## Notes

- All new API calls are **non-fatal** — if they fail, existing org-level data still works
- The `search` query parameter on the metrics endpoint is case-insensitive and matches partial emails
- The API returns cost as a string (e.g., `"971.18"`) representing USD — parse with `double.TryParse` using `InvariantCulture`
- Metrics are fetched for the current calendar month (1st to 1st of next month)
- The user identity picker shows all users (emails + API key names) sorted by cost — user picks theirs after org selection
- `FetchClaudeCodeAllUsers` fetches up to 200 users (sufficient for most orgs); pagination not needed for the picker
- The `DisplayName` property returns `Email ?? ApiKeyName ?? "Unknown"` — used as both the display text and the `search=` value
