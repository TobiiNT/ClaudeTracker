# Feature Gap Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Port 13 features from the macOS Claude Usage Tracker to the Windows ClaudeTracker app, grouped into 5 dependency-based batches.

**Architecture:** Each batch is a vertical slice (model → service → view → test). Business logic is ported verbatim from macOS Swift. UI is adapted to WPF/Material Design. All new services get interfaces and DI registration. All new settings get JSON serialization attributes.

**Tech Stack:** .NET 8, WPF, C#, SkiaSharp, CommunityToolkit.Mvvm, MaterialDesignThemes, Microsoft.Web.WebView2 (new), xUnit/Moq (tests)

**Design doc:** `docs/plans/2026-03-13-feature-gap-migration-design.md`

---

## Batch 1: Usage Intelligence

### Task 1: Effective Session Percentage — Model + Tests

**Files:**
- Modify: `src/ClaudeTracker/Models/ClaudeUsage.cs:72-73`
- Create: `tests/ClaudeTracker.Tests/EffectiveSessionTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/ClaudeTracker.Tests/EffectiveSessionTests.cs
using ClaudeTracker.Models;

namespace ClaudeTracker.Tests;

public class EffectiveSessionTests
{
    [Fact]
    public void EffectiveSessionPercentage_ReturnsZero_WhenSessionExpired()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 75.0,
            SessionResetTime = DateTime.UtcNow.AddMinutes(-10) // expired
        };

        Assert.Equal(0.0, usage.EffectiveSessionPercentage);
    }

    [Fact]
    public void EffectiveSessionPercentage_ReturnsRaw_WhenSessionActive()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 42.5,
            SessionResetTime = DateTime.UtcNow.AddHours(3) // still active
        };

        Assert.Equal(42.5, usage.EffectiveSessionPercentage);
    }

    [Fact]
    public void RemainingPercentage_UsesEffective()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 80.0,
            SessionResetTime = DateTime.UtcNow.AddMinutes(-1) // expired
        };

        // Effective is 0, so remaining is 100
        Assert.Equal(100.0, usage.RemainingPercentage);
    }

    [Fact]
    public void RemainingPercentage_ClampsToZero()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 110.0, // over 100
            SessionResetTime = DateTime.UtcNow.AddHours(1) // active
        };

        Assert.Equal(0.0, usage.RemainingPercentage);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~EffectiveSession" -v minimal`
Expected: Build failure — `EffectiveSessionPercentage` does not exist.

**Step 3: Implement the model changes**

In `src/ClaudeTracker/Models/ClaudeUsage.cs`, add after line 71 (before existing `RemainingPercentage`):

```csharp
[JsonIgnore]
public double EffectiveSessionPercentage =>
    SessionResetTime < DateTime.UtcNow ? 0.0 : SessionPercentage;
```

Change the existing `RemainingPercentage` (line 72-73) from:
```csharp
public double RemainingPercentage => Math.Max(0, 100 - SessionPercentage);
```
to:
```csharp
public double RemainingPercentage => Math.Max(0, 100 - EffectiveSessionPercentage);
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~EffectiveSession" -v minimal`
Expected: 4 passed.

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Models/ClaudeUsage.cs tests/ClaudeTracker.Tests/EffectiveSessionTests.cs
git commit -m "feat: add effective session percentage (auto-zero expired sessions)"
```

---

### Task 2: Wire EffectiveSessionPercentage into Consumers

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs:88-93`
- Modify: `src/ClaudeTracker/Services/NotificationService.cs:20`
- Modify: `src/ClaudeTracker/TrayIcon/TrayIconManager.cs` (find where SessionPercentage is used for icon rendering)

**Step 1: Update PopoverViewModel.RefreshData()**

In `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`, in the `RefreshData()` method, change every `usage.SessionPercentage` to `usage.EffectiveSessionPercentage`:

- Line 89: `SessionPercentage = usage.EffectiveSessionPercentage;`
- Line 91: `UsageStatusCalculator.GetDisplayPercentage(usage.EffectiveSessionPercentage, showRemaining));`
- Line 93: `SessionStatus = UsageStatusCalculator.CalculateStatus(usage.EffectiveSessionPercentage, showRemaining);`

**Step 2: Update NotificationService.CheckAndNotify()**

In `src/ClaudeTracker/Services/NotificationService.cs`, line 20, change:
```csharp
var percentage = usage.SessionPercentage;
```
to:
```csharp
var percentage = usage.EffectiveSessionPercentage;
```

**Step 3: Update TrayIconManager** (search for SessionPercentage usage in icon rendering)

Find and replace any `SessionPercentage` references used for icon rendering with `EffectiveSessionPercentage`. Read TrayIconManager.cs and locate the exact lines.

**Step 4: Build to verify no errors**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 5: Run all tests**

Run: `dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PopoverViewModel.cs src/ClaudeTracker/Services/NotificationService.cs src/ClaudeTracker/TrayIcon/TrayIconManager.cs
git commit -m "refactor: use EffectiveSessionPercentage in all consumers"
```

---

### Task 3: PaceStatus Enum + Calculator + Tests

**Files:**
- Create: `src/ClaudeTracker/Utilities/PaceStatus.cs`
- Create: `tests/ClaudeTracker.Tests/PaceStatusTests.cs`

**Step 1: Write the failing tests**

```csharp
// tests/ClaudeTracker.Tests/PaceStatusTests.cs
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Tests;

public class PaceStatusTests
{
    [Fact]
    public void Calculate_ReturnsNull_WhenElapsedTooLow()
    {
        Assert.Null(PaceStatusCalculator.Calculate(50.0, 0.02));
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenPeriodOver()
    {
        Assert.Null(PaceStatusCalculator.Calculate(50.0, 1.0));
    }

    [Fact]
    public void Calculate_ReturnsComfortable_WhenZeroUsage()
    {
        Assert.Equal(PaceStatus.Comfortable, PaceStatusCalculator.Calculate(0.0, 0.5));
    }

    [Theory]
    [InlineData(10.0, 0.5, PaceStatus.Comfortable)]   // projected 0.2
    [InlineData(25.0, 0.5, PaceStatus.Comfortable)]    // projected 0.5 -> exactly 0.50 boundary
    [InlineData(30.0, 0.5, PaceStatus.OnTrack)]         // projected 0.6
    [InlineData(40.0, 0.5, PaceStatus.Warming)]         // projected 0.8
    [InlineData(47.0, 0.5, PaceStatus.Pressing)]        // projected 0.94
    [InlineData(55.0, 0.5, PaceStatus.Critical)]         // projected 1.1
    [InlineData(70.0, 0.5, PaceStatus.Runaway)]          // projected 1.4
    public void Calculate_ReturnsTier_BasedOnProjection(double used, double elapsed, PaceStatus expected)
    {
        Assert.Equal(expected, PaceStatusCalculator.Calculate(used, elapsed));
    }

    [Fact]
    public void CalculateElapsedFraction_Session_CorrectlyComputes()
    {
        var resetTime = DateTime.UtcNow.AddHours(2.5); // 2.5h remaining of 5h window
        var elapsed = PaceStatusCalculator.CalculateSessionElapsed(resetTime);
        Assert.InRange(elapsed, 0.49, 0.51); // ~50% elapsed
    }

    [Fact]
    public void GetColor_ReturnsCorrectHex()
    {
        Assert.Equal("#4CAF50", PaceStatusCalculator.GetColorHex(PaceStatus.Comfortable));
        Assert.Equal("#9C27B0", PaceStatusCalculator.GetColorHex(PaceStatus.Runaway));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~PaceStatus" -v minimal`
Expected: Build failure — `PaceStatus` and `PaceStatusCalculator` do not exist.

**Step 3: Implement PaceStatus**

```csharp
// src/ClaudeTracker/Utilities/PaceStatus.cs
namespace ClaudeTracker.Utilities;

/// <summary>6-tier pace urgency spectrum. Projects end-of-period usage from current consumption rate.</summary>
public enum PaceStatus
{
    Comfortable = 0,  // projected <50%
    OnTrack = 1,      // projected 50-75%
    Warming = 2,      // projected 75-90%
    Pressing = 3,     // projected 90-100%
    Critical = 4,     // projected 100-120%
    Runaway = 5       // projected >120%
}

public static class PaceStatusCalculator
{
    private const double SessionWindowHours = 5.0;
    private const double WeeklyWindowDays = 7.0;

    /// <summary>
    /// Calculate pace status from current usage and elapsed time fraction.
    /// Returns null when insufficient data (&lt;3% elapsed or period over).
    /// </summary>
    public static PaceStatus? Calculate(double usedPercentage, double elapsedFraction)
    {
        if (elapsedFraction < 0.03 || elapsedFraction >= 1.0)
            return null;

        if (usedPercentage <= 0)
            return PaceStatus.Comfortable;

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

    /// <summary>Calculate elapsed fraction of the 5-hour session window.</summary>
    public static double CalculateSessionElapsed(DateTime resetTime)
    {
        var windowStart = resetTime.AddHours(-SessionWindowHours);
        var totalSeconds = SessionWindowHours * 3600;
        var elapsedSeconds = (DateTime.UtcNow - windowStart).TotalSeconds;
        return Math.Clamp(elapsedSeconds / totalSeconds, 0, 1);
    }

    /// <summary>Calculate elapsed fraction of the 7-day weekly window.</summary>
    public static double CalculateWeeklyElapsed(DateTime resetTime)
    {
        var windowStart = resetTime.AddDays(-WeeklyWindowDays);
        var totalSeconds = WeeklyWindowDays * 24 * 3600;
        var elapsedSeconds = (DateTime.UtcNow - windowStart).TotalSeconds;
        return Math.Clamp(elapsedSeconds / totalSeconds, 0, 1);
    }

    /// <summary>Returns the Material Design hex color for a pace status.</summary>
    public static string GetColorHex(PaceStatus status)
    {
        return status switch
        {
            PaceStatus.Comfortable => "#4CAF50",
            PaceStatus.OnTrack     => "#009688",
            PaceStatus.Warming     => "#FFC107",
            PaceStatus.Pressing    => "#FF9800",
            PaceStatus.Critical    => "#F44336",
            PaceStatus.Runaway     => "#9C27B0",
            _                      => "#4CAF50"
        };
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~PaceStatus" -v minimal`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Utilities/PaceStatus.cs tests/ClaudeTracker.Tests/PaceStatusTests.cs
git commit -m "feat: add PaceStatus 6-tier pace calculation system"
```

---

### Task 4: Integrate Pace + Time Markers into PopoverViewModel

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`

**Step 1: Add pace and elapsed properties to PopoverViewModel**

Add these fields after the existing `[ObservableProperty]` declarations (around line 38):

```csharp
[ObservableProperty] private PaceStatus? _sessionPaceStatus;
[ObservableProperty] private string _sessionPaceLabel = "";
[ObservableProperty] private string _sessionPaceColorHex = "#4CAF50";
[ObservableProperty] private double _sessionElapsedFraction;

[ObservableProperty] private PaceStatus? _weeklyPaceStatus;
[ObservableProperty] private string _weeklyPaceLabel = "";
[ObservableProperty] private string _weeklyPaceColorHex = "#4CAF50";
[ObservableProperty] private double _weeklyElapsedFraction;
```

Add `using ClaudeTracker.Utilities;` at top if not already present.

**Step 2: Compute pace in RefreshData()**

In the `RefreshData()` method, after the session/weekly percentage calculations (around line 99), add:

```csharp
// Pace calculation
var sessionElapsed = PaceStatusCalculator.CalculateSessionElapsed(usage.SessionResetTime);
SessionElapsedFraction = sessionElapsed;
SessionPaceStatus = PaceStatusCalculator.Calculate(usage.EffectiveSessionPercentage, sessionElapsed);
if (SessionPaceStatus.HasValue)
{
    SessionPaceLabel = FormatPaceLabel(SessionPaceStatus.Value);
    SessionPaceColorHex = PaceStatusCalculator.GetColorHex(SessionPaceStatus.Value);
}
else
{
    SessionPaceLabel = "";
}

var weeklyElapsed = PaceStatusCalculator.CalculateWeeklyElapsed(usage.WeeklyResetTime);
WeeklyElapsedFraction = weeklyElapsed;
WeeklyPaceStatus = PaceStatusCalculator.Calculate(usage.WeeklyPercentage, weeklyElapsed);
if (WeeklyPaceStatus.HasValue)
{
    WeeklyPaceLabel = FormatPaceLabel(WeeklyPaceStatus.Value);
    WeeklyPaceColorHex = PaceStatusCalculator.GetColorHex(WeeklyPaceStatus.Value);
}
else
{
    WeeklyPaceLabel = "";
}
```

Add helper method at the bottom of the class:

```csharp
private static string FormatPaceLabel(PaceStatus pace)
{
    return pace switch
    {
        PaceStatus.Comfortable => "Comfortable",
        PaceStatus.OnTrack     => "On Track",
        PaceStatus.Warming     => "Warming",
        PaceStatus.Pressing    => "Pressing",
        PaceStatus.Critical    => "Critical",
        PaceStatus.Runaway     => "Runaway",
        _                      => ""
    };
}
```

In the `else` branch (no usage data, around line 126), also set:
```csharp
SessionPaceStatus = null;
SessionPaceLabel = "";
SessionElapsedFraction = 0;
WeeklyPaceStatus = null;
WeeklyPaceLabel = "";
WeeklyElapsedFraction = 0;
```

**Step 3: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PopoverViewModel.cs
git commit -m "feat: integrate pace status + elapsed fraction into PopoverViewModel"
```

---

### Task 5: Pace Indicators + Time Markers in Popover UI

**Files:**
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml.cs`

**Step 1: Add pace indicator to Session card in XAML**

In `src/ClaudeTracker/Views/PopoverWindow.xaml`, inside the Session card's `<StackPanel>` (after `SessionResetText` on line 82), add:

```xml
<StackPanel x:Name="SessionPacePanel" Orientation="Horizontal" Margin="0,4,0,0" Visibility="Collapsed">
    <Ellipse x:Name="SessionPaceDot" Width="8" Height="8" Margin="0,0,6,0" VerticalAlignment="Center" />
    <TextBlock x:Name="SessionPaceText" FontSize="11" VerticalAlignment="Center" />
</StackPanel>
```

**Step 2: Add time marker overlay to Session progress bar**

Replace the Session progress bar section (lines 78-81) with:

```xml
<Grid Height="7">
    <Border Background="#15000000" CornerRadius="3" ClipToBounds="True">
        <Border x:Name="SessionProgressFill" Background="#4CAF50"
                CornerRadius="3" HorizontalAlignment="Left" Width="0" />
    </Border>
    <Border x:Name="SessionTimeMarker" Width="2" HorizontalAlignment="Left"
            CornerRadius="1" Margin="0,0,0,0"
            Background="#80FFFFFF" Visibility="Collapsed" />
</Grid>
```

**Step 3: Repeat for Weekly card**

After `WeeklyResetText` (line 98), add pace panel:

```xml
<StackPanel x:Name="WeeklyPacePanel" Orientation="Horizontal" Margin="0,4,0,0" Visibility="Collapsed">
    <Ellipse x:Name="WeeklyPaceDot" Width="8" Height="8" Margin="0,0,6,0" VerticalAlignment="Center" />
    <TextBlock x:Name="WeeklyPaceText" FontSize="11" VerticalAlignment="Center" />
</StackPanel>
```

Replace Weekly progress bar (lines 94-97) with:

```xml
<Grid Height="6">
    <Border Background="#15000000" CornerRadius="3" ClipToBounds="True">
        <Border x:Name="WeeklyProgressFill" Background="#4CAF50"
                CornerRadius="3" HorizontalAlignment="Left" Width="0" />
    </Border>
    <Border x:Name="WeeklyTimeMarker" Width="2" HorizontalAlignment="Left"
            CornerRadius="1"
            Background="#80FFFFFF" Visibility="Collapsed" />
</Grid>
```

**Step 4: Update code-behind — pace indicators**

In `src/ClaudeTracker/Views/PopoverWindow.xaml.cs`, in the `UpdateUI()` method (around line 82), add after session reset text:

```csharp
// Session pace
if (!string.IsNullOrEmpty(_viewModel.SessionPaceLabel))
{
    SessionPacePanel.Visibility = Visibility.Visible;
    SessionPaceDot.Fill = BrushFromHex(_viewModel.SessionPaceColorHex);
    SessionPaceText.Text = _viewModel.SessionPaceLabel;
    SessionPaceText.Foreground = BrushFromHex(_viewModel.SessionPaceColorHex);
}
else
{
    SessionPacePanel.Visibility = Visibility.Collapsed;
}

// Weekly pace
if (!string.IsNullOrEmpty(_viewModel.WeeklyPaceLabel))
{
    WeeklyPacePanel.Visibility = Visibility.Visible;
    WeeklyPaceDot.Fill = BrushFromHex(_viewModel.WeeklyPaceColorHex);
    WeeklyPaceText.Text = _viewModel.WeeklyPaceLabel;
    WeeklyPaceText.Foreground = BrushFromHex(_viewModel.WeeklyPaceColorHex);
}
else
{
    WeeklyPacePanel.Visibility = Visibility.Collapsed;
}
```

**Step 5: Update code-behind — time markers**

In `UpdateProgressBars()` method, add after the existing `SetProgressWidth` calls:

```csharp
// Time markers
SetTimeMarker(SessionTimeMarker, SessionProgressFill, _viewModel.SessionElapsedFraction);
SetTimeMarker(WeeklyTimeMarker, WeeklyProgressFill, _viewModel.WeeklyElapsedFraction);
```

Add helper methods:

```csharp
private static void SetTimeMarker(FrameworkElement marker, FrameworkElement progressFill, double elapsedFraction)
{
    if (elapsedFraction > 0.03 && elapsedFraction < 1.0 && progressFill.Parent is FrameworkElement parent && parent.Parent is FrameworkElement grandParent && grandParent.ActualWidth > 0)
    {
        marker.Visibility = Visibility.Visible;
        marker.Margin = new Thickness(grandParent.ActualWidth * elapsedFraction - 1, 0, 0, 0);
    }
    else
    {
        marker.Visibility = Visibility.Collapsed;
    }
}

private static SolidColorBrush BrushFromHex(string hex)
{
    hex = hex.TrimStart('#');
    if (hex.Length == 6 &&
        byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
        byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
        byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
    {
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }
    return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
}
```

**Step 6: Build and run all tests**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal && dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: Build succeeded, all tests pass.

**Step 7: Commit**

```bash
git add src/ClaudeTracker/Views/PopoverWindow.xaml src/ClaudeTracker/Views/PopoverWindow.xaml.cs
git commit -m "feat: add pace indicators and time markers to popover progress bars"
```

---

## Batch 2: Status & Resilience

### Task 6: ClaudeStatus Model + ClaudeStatusService + Tests

**Files:**
- Create: `src/ClaudeTracker/Models/ClaudeStatus.cs`
- Create: `src/ClaudeTracker/Services/Interfaces/IClaudeStatusService.cs`
- Create: `src/ClaudeTracker/Services/ClaudeStatusService.cs`
- Create: `tests/ClaudeTracker.Tests/ClaudeStatusTests.cs`
- Modify: `src/ClaudeTracker/Utilities/Constants.cs`

**Step 1: Write the failing tests**

```csharp
// tests/ClaudeTracker.Tests/ClaudeStatusTests.cs
using ClaudeTracker.Models;

namespace ClaudeTracker.Tests;

public class ClaudeStatusTests
{
    [Fact]
    public void Unknown_HasCorrectDefaults()
    {
        var status = ClaudeStatus.Unknown;
        Assert.Equal(StatusIndicator.Unknown, status.Indicator);
        Assert.Equal("Status Unknown", status.Description);
    }

    [Fact]
    public void Operational_HasCorrectDefaults()
    {
        var status = ClaudeStatus.Operational;
        Assert.Equal(StatusIndicator.None, status.Indicator);
        Assert.Equal("All Systems Operational", status.Description);
    }

    [Theory]
    [InlineData(StatusIndicator.None, "#4CAF50")]
    [InlineData(StatusIndicator.Minor, "#FFC107")]
    [InlineData(StatusIndicator.Major, "#FF9800")]
    [InlineData(StatusIndicator.Critical, "#F44336")]
    [InlineData(StatusIndicator.Unknown, "#9E9E9E")]
    public void GetColorHex_ReturnsCorrectColor(StatusIndicator indicator, string expectedHex)
    {
        Assert.Equal(expectedHex, ClaudeStatus.GetColorHex(indicator));
    }

    [Theory]
    [InlineData("none", StatusIndicator.None)]
    [InlineData("minor", StatusIndicator.Minor)]
    [InlineData("major", StatusIndicator.Major)]
    [InlineData("critical", StatusIndicator.Critical)]
    [InlineData("banana", StatusIndicator.Unknown)]
    public void ParseIndicator_MapsCorrectly(string input, StatusIndicator expected)
    {
        Assert.Equal(expected, ClaudeStatus.ParseIndicator(input));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~ClaudeStatusTests" -v minimal`
Expected: Build failure.

**Step 3: Implement ClaudeStatus model**

```csharp
// src/ClaudeTracker/Models/ClaudeStatus.cs
namespace ClaudeTracker.Models;

public enum StatusIndicator
{
    None,       // All systems operational
    Minor,      // Minor issues
    Major,      // Major outage
    Critical,   // Critical outage
    Unknown     // Unable to fetch
}

public record ClaudeStatus(StatusIndicator Indicator, string Description)
{
    public static ClaudeStatus Unknown => new(StatusIndicator.Unknown, "Status Unknown");
    public static ClaudeStatus Operational => new(StatusIndicator.None, "All Systems Operational");

    public static string GetColorHex(StatusIndicator indicator) => indicator switch
    {
        StatusIndicator.None     => "#4CAF50",
        StatusIndicator.Minor    => "#FFC107",
        StatusIndicator.Major    => "#FF9800",
        StatusIndicator.Critical => "#F44336",
        StatusIndicator.Unknown  => "#9E9E9E",
        _                        => "#9E9E9E"
    };

    public static StatusIndicator ParseIndicator(string value) => value switch
    {
        "none"     => StatusIndicator.None,
        "minor"    => StatusIndicator.Minor,
        "major"    => StatusIndicator.Major,
        "critical" => StatusIndicator.Critical,
        _          => StatusIndicator.Unknown
    };
}
```

**Step 4: Implement interface + service**

```csharp
// src/ClaudeTracker/Services/Interfaces/IClaudeStatusService.cs
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IClaudeStatusService
{
    Task<ClaudeStatus> FetchStatusAsync();
}
```

```csharp
// src/ClaudeTracker/Services/ClaudeStatusService.cs
using System.Net.Http;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeStatusService : IClaudeStatusService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClaudeStatusService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClaudeStatus> FetchStatusAsync()
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetStringAsync(Constants.StatusAPI.StatusUrl);
            using var doc = JsonDocument.Parse(response);
            var statusObj = doc.RootElement.GetProperty("status");
            var indicator = statusObj.GetProperty("indicator").GetString() ?? "unknown";
            var description = statusObj.GetProperty("description").GetString() ?? "Status Unknown";

            return new ClaudeStatus(ClaudeStatus.ParseIndicator(indicator), description);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to fetch Claude status", ex);
            return ClaudeStatus.Unknown;
        }
    }
}
```

**Step 5: Add Constants**

In `src/ClaudeTracker/Utilities/Constants.cs`, add before the closing brace of the `Constants` class:

```csharp
public static class StatusAPI
{
    public const string StatusUrl = "https://status.claude.com/api/v2/status.json";
    public const double RefreshIntervalMinutes = 5.0;
}
```

**Step 6: Run tests**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~ClaudeStatusTests" -v minimal`
Expected: All pass.

**Step 7: Commit**

```bash
git add src/ClaudeTracker/Models/ClaudeStatus.cs src/ClaudeTracker/Services/Interfaces/IClaudeStatusService.cs src/ClaudeTracker/Services/ClaudeStatusService.cs src/ClaudeTracker/Utilities/Constants.cs tests/ClaudeTracker.Tests/ClaudeStatusTests.cs
git commit -m "feat: add ClaudeStatusService for live system status indicator"
```

---

### Task 7: NetworkMonitorService

**Files:**
- Create: `src/ClaudeTracker/Services/Interfaces/INetworkMonitorService.cs`
- Create: `src/ClaudeTracker/Services/NetworkMonitorService.cs`

**Step 1: Implement interface**

```csharp
// src/ClaudeTracker/Services/Interfaces/INetworkMonitorService.cs
namespace ClaudeTracker.Services.Interfaces;

public interface INetworkMonitorService : IDisposable
{
    void Start();
    event EventHandler? NetworkRestored;
}
```

**Step 2: Implement service**

```csharp
// src/ClaudeTracker/Services/NetworkMonitorService.cs
using System.Net.NetworkInformation;
using System.Windows.Threading;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Services;

public class NetworkMonitorService : INetworkMonitorService
{
    private bool _wasAvailable = true;
    private DispatcherTimer? _debounceTimer;

    public event EventHandler? NetworkRestored;

    public void Start()
    {
        _wasAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        LoggingService.Instance.Log($"NetworkMonitor started (available: {_wasAvailable})");
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        LoggingService.Instance.Log($"Network availability changed: {e.IsAvailable}");

        if (e.IsAvailable && !_wasAvailable)
        {
            // Network restored — debounce 3 seconds to avoid flapping
            _debounceTimer?.Stop();
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                LoggingService.Instance.Log("Network restored — triggering refresh");
                NetworkRestored?.Invoke(this, EventArgs.Empty);
            };
            _debounceTimer.Start();
        }

        _wasAvailable = e.IsAvailable;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _debounceTimer?.Stop();
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/ClaudeTracker/Services/Interfaces/INetworkMonitorService.cs src/ClaudeTracker/Services/NetworkMonitorService.cs
git commit -m "feat: add NetworkMonitorService with auto-refresh on reconnect"
```

---

### Task 8: Wire Status + Network into DI, Coordinator, and Popover

**Files:**
- Modify: `src/ClaudeTracker/App.xaml.cs:124-165` (ConfigureServices + OnStartup)
- Modify: `src/ClaudeTracker/Services/UsageRefreshCoordinator.cs`
- Modify: `src/ClaudeTracker/Services/Interfaces/IUsageRefreshCoordinator.cs`
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml.cs`

**Step 1: DI registration in App.xaml.cs**

In `ConfigureServices()`, after line 140 (`services.AddSingleton<LanguageService>();`), add:

```csharp
services.AddSingleton<IClaudeStatusService, ClaudeStatusService>();
services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();
```

Add `using ClaudeTracker.Services.Interfaces;` at top if not present.

In `OnStartup()`, after the global hotkey registration (line 66), add:

```csharp
// Start network monitor
var networkMonitor = _services.GetRequiredService<INetworkMonitorService>();
networkMonitor.NetworkRestored += (_, _) => refreshCoordinator.RefreshNow();
networkMonitor.Start();
```

**Step 2: Add status fetch to UsageRefreshCoordinator**

Add `IClaudeStatusService` to the constructor:

```csharp
private readonly IClaudeStatusService _statusService;
private ClaudeStatus _cachedStatus = ClaudeStatus.Unknown;
private DateTime _lastStatusFetch = DateTime.MinValue;

public ClaudeStatus CurrentStatus => _cachedStatus;
```

Update constructor signature to include `IClaudeStatusService statusService` and assign `_statusService = statusService;`.

In `RefreshAsync()`, add before the `RefreshCompleted` event (around line 101):

```csharp
// Fetch Claude system status (every 5 minutes)
if ((DateTime.UtcNow - _lastStatusFetch).TotalMinutes >= Constants.StatusAPI.RefreshIntervalMinutes)
{
    _cachedStatus = await _statusService.FetchStatusAsync();
    _lastStatusFetch = DateTime.UtcNow;
}
```

Add `CurrentStatus` to `IUsageRefreshCoordinator`:

```csharp
ClaudeStatus CurrentStatus { get; }
```

**Step 3: Add status to PopoverViewModel**

Add properties:

```csharp
[ObservableProperty] private string _claudeStatusDescription = "";
[ObservableProperty] private string _claudeStatusColorHex = "#9E9E9E";
[ObservableProperty] private bool _showClaudeStatus;
```

In `RefreshData()`, add:

```csharp
// Claude system status
var status = _refreshCoordinator.CurrentStatus;
ClaudeStatusDescription = status.Description;
ClaudeStatusColorHex = ClaudeStatus.GetColorHex(status.Indicator);
ShowClaudeStatus = status.Indicator != StatusIndicator.None;
```

Note: only show the status row when there's an issue (not when operational — save space).

**Step 4: Add status indicator to PopoverWindow.xaml**

In the popover XAML, after the profile switcher `<Grid>` (around line 68), add:

```xml
<!-- Claude Status Indicator (shown only during issues) -->
<Border x:Name="ClaudeStatusPanel" Visibility="Collapsed"
        Background="#10FF9800" CornerRadius="6" Padding="8,5" Margin="0,0,0,6">
    <StackPanel Orientation="Horizontal">
        <Ellipse x:Name="ClaudeStatusDot" Width="8" Height="8" Margin="0,0,6,0" VerticalAlignment="Center" />
        <TextBlock x:Name="ClaudeStatusText" FontSize="12" VerticalAlignment="Center" />
    </StackPanel>
</Border>
```

**Step 5: Wire status in PopoverWindow.xaml.cs**

In `UpdateUI()`, add:

```csharp
// Claude system status
if (_viewModel.ShowClaudeStatus)
{
    ClaudeStatusPanel.Visibility = Visibility.Visible;
    ClaudeStatusDot.Fill = BrushFromHex(_viewModel.ClaudeStatusColorHex);
    ClaudeStatusText.Text = _viewModel.ClaudeStatusDescription;
    ClaudeStatusText.Foreground = BrushFromHex(_viewModel.ClaudeStatusColorHex);
}
else
{
    ClaudeStatusPanel.Visibility = Visibility.Collapsed;
}
```

**Step 6: Build and test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal && dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: Build succeeded, all tests pass.

**Step 7: Commit**

```bash
git add src/ClaudeTracker/App.xaml.cs src/ClaudeTracker/Services/UsageRefreshCoordinator.cs src/ClaudeTracker/Services/Interfaces/IUsageRefreshCoordinator.cs src/ClaudeTracker/ViewModels/PopoverViewModel.cs src/ClaudeTracker/Views/PopoverWindow.xaml src/ClaudeTracker/Views/PopoverWindow.xaml.cs
git commit -m "feat: wire Claude status indicator and network monitor into UI"
```

---

## Batch 3: Auth & Expiry

### Task 9: Session Key Expiry — Model + Notification

**Files:**
- Modify: `src/ClaudeTracker/Models/Profile.cs`
- Modify: `src/ClaudeTracker/Services/NotificationService.cs`
- Modify: `src/ClaudeTracker/Services/Interfaces/INotificationService.cs`

**Step 1: Add expiry fields to Profile**

In `src/ClaudeTracker/Models/Profile.cs`, after line 28 (`CliCredentialsJSON`), add:

```csharp
[JsonPropertyName("claudeSessionKeyExpiry")]
public DateTime? ClaudeSessionKeyExpiry { get; set; }

[JsonPropertyName("apiSessionKeyExpiry")]
public DateTime? ApiSessionKeyExpiry { get; set; }
```

**Step 2: Add expiry checking to NotificationService**

In `INotificationService`, add:

```csharp
void CheckKeyExpiry(Profile profile);
```

In `NotificationService`, add:

```csharp
private readonly HashSet<string> _sentExpiryNotifications = new();

public void CheckKeyExpiry(Profile profile)
{
    CheckSingleKeyExpiry(profile, "claude", profile.ClaudeSessionKeyExpiry);
    CheckSingleKeyExpiry(profile, "api", profile.ApiSessionKeyExpiry);
}

private void CheckSingleKeyExpiry(Profile profile, string keyType, DateTime? expiry)
{
    if (!expiry.HasValue) return;

    var remaining = expiry.Value - DateTime.UtcNow;
    if (remaining.TotalHours > 24) return;

    var expiryKey = $"{profile.Id}_expiry_{keyType}";
    if (_sentExpiryNotifications.Contains(expiryKey)) return;

    var title = $"Session Key Expiring — {profile.Name}";
    var message = remaining.TotalHours > 0
        ? $"Your {keyType} session key expires in {(int)remaining.TotalHours}h {remaining.Minutes}m. Re-authenticate to avoid interruption."
        : $"Your {keyType} session key has expired. Re-authenticate to continue tracking.";

    SendNotification(title, message, Views.NotificationPopup.NotificationLevel.Warning);
    _sentExpiryNotifications.Add(expiryKey);
}

public void ResetExpiryNotifications(Guid profileId, string keyType)
{
    _sentExpiryNotifications.Remove($"{profileId}_expiry_{keyType}");
}
```

**Step 3: Call CheckKeyExpiry from UsageRefreshCoordinator**

In `UsageRefreshCoordinator.RefreshAsync()`, after the notification check (line 90), add:

```csharp
_notificationService.CheckKeyExpiry(profile);
```

Note: `INotificationService` needs the `CheckKeyExpiry` method added.

**Step 4: Build and test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal && dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: Build succeeded, all tests pass.

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Models/Profile.cs src/ClaudeTracker/Services/NotificationService.cs src/ClaudeTracker/Services/Interfaces/INotificationService.cs src/ClaudeTracker/Services/UsageRefreshCoordinator.cs
git commit -m "feat: add session key expiry tracking with 24-hour advance notification"
```

---

### Task 10: WebView2 Browser Sign-In

**Files:**
- Modify: `src/ClaudeTracker/ClaudeTracker.csproj` (NuGet)
- Create: `src/ClaudeTracker/Views/BrowserSignInWindow.xaml`
- Create: `src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs`
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml`
- Modify: `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs`

**Step 1: Add WebView2 NuGet**

Run: `dotnet add src/ClaudeTracker/ClaudeTracker.csproj package Microsoft.Web.WebView2`

**Step 2: Create BrowserSignInWindow.xaml**

```xml
<!-- src/ClaudeTracker/Views/BrowserSignInWindow.xaml -->
<Window x:Class="ClaudeTracker.Views.BrowserSignInWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Sign in to Claude" Width="500" Height="700"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="True">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Orientation="Horizontal" Margin="12,8">
            <TextBlock x:Name="StatusText" FontSize="12" VerticalAlignment="Center"
                       Foreground="#888" Text="Waiting for sign-in..." />
        </StackPanel>

        <wv2:WebView2 x:Name="WebView" Grid.Row="1" />
    </Grid>
</Window>
```

**Step 3: Create BrowserSignInWindow.xaml.cs**

```csharp
// src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ClaudeTracker.Services;

namespace ClaudeTracker.Views;

public partial class BrowserSignInWindow : Window
{
    private readonly string _targetUrl;
    private readonly string _cookieDomain;
    private readonly TaskCompletionSource<(string sessionKey, DateTime? expiry)?> _result = new();

    public Task<(string sessionKey, DateTime? expiry)?> ResultTask => _result.Task;

    public BrowserSignInWindow(string targetUrl, string cookieDomain)
    {
        InitializeComponent();
        _targetUrl = targetUrl;
        _cookieDomain = cookieDomain;
        Loaded += OnLoaded;
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch { return false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("WebView2 init failed", ex);
            StatusText.Text = "WebView2 initialization failed";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(_targetUrl);
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "sessionKey" && cookie.Domain.Contains(_cookieDomain))
                {
                    DateTime? expiry = cookie.Expires > DateTime.MinValue ? cookie.Expires.ToUniversalTime() : null;
                    StatusText.Text = "Session key captured!";
                    _result.TrySetResult((cookie.Value, expiry));
                    await Task.Delay(500); // Brief visual feedback
                    Dispatcher.Invoke(Close);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Cookie extraction failed", ex);
        }
    }
}
```

**Step 4: Add browser sign-in buttons to PersonalUsageView.xaml**

In `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml`, in the `SetupPanel` `<StackPanel>` (line 31), before the existing "Claude Code CLI" card (line 34), add:

```xml
<!-- Option 0: Browser sign-in (shown when WebView2 available) -->
<Border x:Name="BrowserSignInCard" Style="{StaticResource UsageCardStyle}" Padding="16" Margin="0,0,0,8"
        Visibility="Collapsed">
    <StackPanel>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
            <materialDesign:PackIcon Kind="Web" Width="18" Height="18"
                                     Foreground="#2196F3" Margin="0,0,8,0" VerticalAlignment="Center" />
            <TextBlock Text="Sign in with Browser" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center" />
        </StackPanel>
        <TextBlock Text="Sign in directly — session key is captured automatically"
                   FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"
                   Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
        <Button x:Name="BrowserSignInButton" Content="Open Browser"
                Style="{StaticResource MaterialDesignRaisedButton}"
                HorizontalAlignment="Left" Height="34" FontSize="12" />
    </StackPanel>
</Border>
```

Do the same for the API Billing section — add a `BrowserApiSignInCard` before `ApiSetupPanel` (line 142).

**Step 5: Wire buttons in PersonalUsageView.xaml.cs**

In the constructor, add WebView2 detection and button click handlers. The exact integration depends on how PersonalUsageView.xaml.cs is currently structured — read it and add the browser sign-in flow that calls `BrowserSignInWindow`, captures the result, and saves credentials via the existing `IProfileService`.

Key logic:
```csharp
if (BrowserSignInWindow.IsWebView2Available())
    BrowserSignInCard.Visibility = Visibility.Visible;

BrowserSignInButton.Click += async (_, _) =>
{
    var window = new BrowserSignInWindow("https://claude.ai/login", "claude.ai");
    window.Owner = Window.GetWindow(this);
    window.Show();
    var result = await window.ResultTask;
    if (result.HasValue)
    {
        // Test and save the session key using existing PersonalUsageViewModel flow
    }
};
```

**Step 6: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 7: Commit**

```bash
git add src/ClaudeTracker/ClaudeTracker.csproj src/ClaudeTracker/Views/BrowserSignInWindow.xaml src/ClaudeTracker/Views/BrowserSignInWindow.xaml.cs src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs
git commit -m "feat: add WebView2 browser sign-in with runtime detection and fallback"
```

---

## Batch 4: Settings & Polish

### Task 11: TimeFormatPreference Model + FormatterHelper Updates + Tests

**Files:**
- Create: `src/ClaudeTracker/Models/TimeFormatPreference.cs`
- Modify: `src/ClaudeTracker/Models/AppSettings.cs`
- Modify: `src/ClaudeTracker/Utilities/FormatterHelper.cs`
- Create: `tests/ClaudeTracker.Tests/TimeFormatTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ClaudeTracker.Tests/TimeFormatTests.cs
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Tests;

public class TimeFormatTests
{
    [Fact]
    public void FormatResetTimeAbsolute_Today_12Hour()
    {
        var today = DateTime.Now.Date.AddHours(15).AddMinutes(30);
        var result = FormatterHelper.FormatResetTimeAbsolute(today.ToUniversalTime(), use24Hour: false);
        Assert.StartsWith("Today", result);
        Assert.Contains("PM", result);
    }

    [Fact]
    public void FormatResetTimeAbsolute_Today_24Hour()
    {
        var today = DateTime.Now.Date.AddHours(15).AddMinutes(30);
        var result = FormatterHelper.FormatResetTimeAbsolute(today.ToUniversalTime(), use24Hour: true);
        Assert.StartsWith("Today", result);
        Assert.Contains("15:", result);
    }

    [Fact]
    public void FormatResetTimeCombined_ShowsBoth()
    {
        var future = DateTime.UtcNow.AddHours(2);
        var result = FormatterHelper.FormatResetTimeCombined(future, use24Hour: false);
        Assert.Contains("(", result); // has parenthesized absolute time
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~TimeFormat" -v minimal`
Expected: Build failure.

**Step 3: Create TimeFormatPreference model**

```csharp
// src/ClaudeTracker/Models/TimeFormatPreference.cs
namespace ClaudeTracker.Models;

public enum TimeFormatPreference
{
    System,
    TwelveHour,
    TwentyFourHour
}

public enum PopoverTimeDisplay
{
    ResetTime,
    RemainingTime,
    Both
}
```

**Step 4: Add settings to AppSettings**

In `src/ClaudeTracker/Models/AppSettings.cs`, add before the closing brace:

```csharp
[JsonPropertyName("popoverTimeDisplay")]
public string PopoverTimeDisplay { get; set; } = "remainingTime";

[JsonPropertyName("timeFormatPreference")]
public string TimeFormatPreference { get; set; } = "system";
```

**Step 5: Add formatter methods**

In `src/ClaudeTracker/Utilities/FormatterHelper.cs`, add:

```csharp
/// <summary>Formats reset time as absolute ("Today 3:59 PM" or "Today 15:59").</summary>
public static string FormatResetTimeAbsolute(DateTime resetTimeUtc, bool use24Hour)
{
    var local = resetTimeUtc.ToLocalTime();
    var now = DateTime.Now;
    var timeFormat = use24Hour ? "H:mm" : "h:mm tt";

    var prefix = local.Date == now.Date ? "Today"
        : local.Date == now.Date.AddDays(1) ? "Tomorrow"
        : local.ToString("ddd, MMM d");

    return $"{prefix} {local.ToString(timeFormat)}";
}

/// <summary>Formats reset time as "Resets in Xh Ym (Today 3:59 PM)".</summary>
public static string FormatResetTimeCombined(DateTime resetTimeUtc, bool use24Hour)
{
    var remaining = FormatTimeRemaining(resetTimeUtc);
    var absolute = FormatResetTimeAbsolute(resetTimeUtc, use24Hour);
    return $"{remaining} ({absolute})";
}

/// <summary>Determines if the system locale uses 24-hour time.</summary>
public static bool IsSystem24Hour()
{
    var pattern = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
    return pattern.Contains('H');
}
```

**Step 6: Run tests**

Run: `dotnet test tests/ClaudeTracker.Tests --filter "FullyQualifiedName~TimeFormat" -v minimal`
Expected: All pass.

**Step 7: Commit**

```bash
git add src/ClaudeTracker/Models/TimeFormatPreference.cs src/ClaudeTracker/Models/AppSettings.cs src/ClaudeTracker/Utilities/FormatterHelper.cs tests/ClaudeTracker.Tests/TimeFormatTests.cs
git commit -m "feat: add time format preferences and formatter methods"
```

---

### Task 12: Wire Time Format into PopoverViewModel + GeneralSettings

**Files:**
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs`
- Modify: `src/ClaudeTracker/ViewModels/GeneralSettingsViewModel.cs`
- Modify: `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml`
- Modify: `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml.cs`

**Step 1: Update PopoverViewModel to use time format settings**

Add `ISettingsService` to the PopoverViewModel constructor. In `RefreshData()`, replace the session/weekly reset text formatting:

```csharp
var settings = _settingsService.Settings;
var use24Hour = settings.TimeFormatPreference switch
{
    "twelveHour" => false,
    "twentyFourHour" => true,
    _ => FormatterHelper.IsSystem24Hour()
};

var timeDisplay = settings.PopoverTimeDisplay;
SessionResetText = FormatResetText(usage.SessionResetTime, timeDisplay, use24Hour);
WeeklyResetText = FormatResetText(usage.WeeklyResetTime, timeDisplay, use24Hour);
```

Add helper:
```csharp
private static string FormatResetText(DateTime resetTime, string displayMode, bool use24Hour)
{
    return displayMode switch
    {
        "resetTime" => $"Resets {FormatterHelper.FormatResetTimeAbsolute(resetTime, use24Hour)}",
        "both" => $"Resets {FormatterHelper.FormatResetTimeCombined(resetTime, use24Hour)}",
        _ => $"Resets {FormatterHelper.FormatTimeRemaining(resetTime)}"
    };
}
```

**Step 2: Add time settings to GeneralSettingsViewModel**

Add properties:

```csharp
[ObservableProperty] private string _popoverTimeDisplay;
[ObservableProperty] private string _timeFormatPreference;

// Add initial snapshots
private string _initialPopoverTimeDisplay;
private string _initialTimeFormatPreference;
```

Initialize from `_settingsService.Settings` in the constructor. Add to `DetectChanges()` and `Save()`.

In `Save()`, save to `_settingsService.Settings`:
```csharp
var settings = _settingsService.Settings;
settings.PopoverTimeDisplay = PopoverTimeDisplay;
settings.TimeFormatPreference = TimeFormatPreference;
_settingsService.Save();
```

**Step 3: Add time settings UI to GeneralSettingsView.xaml**

After the "Startup" section (line 54), add:

```xml
<!-- Time & Display -->
<TextBlock Text="Time &amp; Display" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,8" />
<Grid Margin="0,6,0,0">
    <TextBlock Text="Reset time format" VerticalAlignment="Center" FontSize="13" />
    <ComboBox x:Name="TimeDisplayCombo" HorizontalAlignment="Right" Width="160"
              Style="{StaticResource MaterialDesignComboBox}" FontSize="12" />
</Grid>
<Grid Margin="0,10,0,20">
    <TextBlock Text="Time format" VerticalAlignment="Center" FontSize="13" />
    <ComboBox x:Name="TimeFormatCombo" HorizontalAlignment="Right" Width="160"
              Style="{StaticResource MaterialDesignComboBox}" FontSize="12" />
</Grid>
```

**Step 4: Wire combos in GeneralSettingsView.xaml.cs**

```csharp
// Time display combo
TimeDisplayCombo.ItemsSource = new[] {
    new { Display = "Remaining Time", Value = "remainingTime" },
    new { Display = "Reset Time", Value = "resetTime" },
    new { Display = "Both", Value = "both" }
};
TimeDisplayCombo.DisplayMemberPath = "Display";
TimeDisplayCombo.SelectedValuePath = "Value";
TimeDisplayCombo.SelectedValue = _vm.PopoverTimeDisplay;
TimeDisplayCombo.SelectionChanged += (_, _) =>
{
    if (TimeDisplayCombo.SelectedValue is string v) _vm.PopoverTimeDisplay = v;
};

// Time format combo
TimeFormatCombo.ItemsSource = new[] {
    new { Display = "System Default", Value = "system" },
    new { Display = "12-Hour (3:59 PM)", Value = "twelveHour" },
    new { Display = "24-Hour (15:59)", Value = "twentyFourHour" }
};
TimeFormatCombo.DisplayMemberPath = "Display";
TimeFormatCombo.SelectedValuePath = "Value";
TimeFormatCombo.SelectedValue = _vm.TimeFormatPreference;
TimeFormatCombo.SelectionChanged += (_, _) =>
{
    if (TimeFormatCombo.SelectedValue is string v) _vm.TimeFormatPreference = v;
};
```

**Step 5: Build and test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal && dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: Build succeeded, all tests pass.

**Step 6: Commit**

```bash
git add src/ClaudeTracker/ViewModels/PopoverViewModel.cs src/ClaudeTracker/ViewModels/GeneralSettingsViewModel.cs src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml.cs
git commit -m "feat: wire time format preferences into popover and settings UI"
```

---

### Task 13: Wire ShowIconNames in TrayIconRenderer

**Files:**
- Modify: `src/ClaudeTracker/TrayIcon/TrayIconRenderer.cs:147-162`

**Step 1: Update DrawPercentage to support icon name prefix**

Modify `DrawPercentage()` to accept an optional prefix parameter. When `showIconNames` is true and a prefix is provided, prepend it:

```csharp
private void DrawPercentage(SKCanvas canvas, int size, double percentage, SKColor fillColor, SKColor outlineColor, string? prefix = null)
{
    var text = prefix != null ? $"{prefix}{(int)Math.Round(percentage)}" : $"{(int)Math.Round(percentage)}";
    // rest remains the same, but reduce font size slightly when prefix present
    var fontSize = prefix != null ? size * 0.50f : size * 0.65f;
    // ...
}
```

**Step 2: Update RenderIcon to pass prefix**

Add `showIconNames` and `metricType` parameters to `RenderIcon()`. When `showIconNames` is true and style is `Percentage`, pass the appropriate prefix ("S:", "W:", "A:").

**Step 3: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/ClaudeTracker/TrayIcon/TrayIconRenderer.cs
git commit -m "feat: wire ShowIconNames toggle to tray icon rendering"
```

---

### Task 14: Wire Setup Wizard on First Launch

**Files:**
- Modify: `src/ClaudeTracker/App.xaml.cs:46-57`

**Step 1: Add setup wizard check**

In `App.xaml.cs`, in `OnStartup()`, after `Services = _services;` (line 46) and before `InitializeTheme()` (line 49), add:

```csharp
// Show setup wizard on first launch
var settingsService = _services.GetRequiredService<ISettingsService>();
if (!settingsService.Settings.HasCompletedSetup)
{
    var wizard = new Views.SetupWizardWindow();
    wizard.ShowDialog();
}
```

**Step 2: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/ClaudeTracker/App.xaml.cs
git commit -m "fix: wire setup wizard to show on first launch"
```

---

### Task 15: XAML i18n for Modified Views

**Files:**
- Modify: `src/ClaudeTracker/Localization/Strings.resx`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`
- Modify: `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml`

**Step 1: Add string keys to Strings.resx**

Open `src/ClaudeTracker/Localization/Strings.resx` and add keys for hardcoded strings in the views we've modified. Key examples:

- `SessionUsage` = "Session Usage"
- `WeeklyUsage` = "Weekly Usage"
- `OverageCost` = "Overage Cost"
- `ApiCredits` = "API Credits"
- `NoCredentials` = "No credentials configured"
- `OpenSettings` = "Open Settings to get started"
- `RefreshInterval` = "Refresh Interval"
- `Startup` = "Startup"
- `Notifications` = "Notifications"
- `TimeAndDisplay` = "Time & Display"
- `General` = "General"
- `ResetTimeFormat` = "Reset time format"
- `TimeFormat` = "Time format"
- `RemainingTime` = "Remaining Time"
- `ResetTime` = "Reset Time"
- `Both` = "Both"

**Step 2: Replace hardcoded strings in XAML**

Add namespace to each XAML:
```xml
xmlns:localization="clr-namespace:ClaudeTracker.Localization"
```

Replace strings:
```xml
<!-- Before -->
<TextBlock Text="Session Usage" />
<!-- After -->
<TextBlock Text="{x:Static localization:Strings.SessionUsage}" />
```

Only touch the views we've modified in this batch (PopoverWindow.xaml, GeneralSettingsView.xaml).

**Step 3: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/ClaudeTracker/Localization/Strings.resx src/ClaudeTracker/Views/PopoverWindow.xaml src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml
git commit -m "refactor: replace hardcoded strings with resx bindings in modified views"
```

---

## Batch 5: Engagement & Polish

### Task 16: Notification Sounds

**Files:**
- Modify: `src/ClaudeTracker/Models/NotificationSettings.cs`
- Modify: `src/ClaudeTracker/Services/NotificationService.cs`
- Modify: `src/ClaudeTracker/ViewModels/GeneralSettingsViewModel.cs`
- Modify: `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml`
- Modify: `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml.cs`

**Step 1: Add sound settings to model**

In `src/ClaudeTracker/Models/NotificationSettings.cs`, add:

```csharp
[JsonPropertyName("soundEnabled")]
public bool SoundEnabled { get; set; } = true;

[JsonPropertyName("soundName")]
public string SoundName { get; set; } = "Default";
```

**Step 2: Play sound in NotificationService**

In `SendNotification()`, after `popup.Show();`, add:

```csharp
if (profile?.NotificationSettings.SoundEnabled == true)
{
    PlaySound(profile.NotificationSettings.SoundName);
}
```

Add helper:
```csharp
private static void PlaySound(string soundName)
{
    try
    {
        var sound = soundName switch
        {
            "Hand" => System.Media.SystemSounds.Hand,
            "Asterisk" => System.Media.SystemSounds.Asterisk,
            "Question" => System.Media.SystemSounds.Question,
            "Beep" => System.Media.SystemSounds.Beep,
            "None" => null,
            _ => System.Media.SystemSounds.Exclamation // Default
        };
        sound?.Play();
    }
    catch { /* ignore sound failures */ }
}
```

Note: The `SendNotification()` method that sends threshold alerts needs access to the profile to check sound settings. Modify `SendThresholdNotification()` to pass through the profile reference, or store a reference to the current profile.

**Step 3: Add sound controls to GeneralSettingsView**

After the "Test Alert" button (line 86), add:

```xml
<!-- Notification Sound -->
<Grid Margin="16,10,0,0">
    <TextBlock Text="Notification sound" VerticalAlignment="Center" FontSize="12" />
    <ToggleButton x:Name="SoundToggle" HorizontalAlignment="Right"
                  Style="{StaticResource CompactSwitch}" />
</Grid>
<Grid x:Name="SoundPickerPanel" Margin="16,8,0,0">
    <TextBlock Text="Sound" VerticalAlignment="Center" FontSize="12" />
    <ComboBox x:Name="SoundCombo" HorizontalAlignment="Right" Width="140"
              Style="{StaticResource MaterialDesignComboBox}" FontSize="12" />
</Grid>
```

Wire in code-behind with items: Default, Hand, Asterisk, Question, Beep, None.

**Step 4: Build and test**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal && dotnet test tests/ClaudeTracker.Tests -v minimal`
Expected: Build succeeded, all tests pass.

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Models/NotificationSettings.cs src/ClaudeTracker/Services/NotificationService.cs src/ClaudeTracker/ViewModels/GeneralSettingsViewModel.cs src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml.cs
git commit -m "feat: add notification sound selection with system sounds"
```

---

### Task 17: GitHub Star Prompt

**Files:**
- Modify: `src/ClaudeTracker/Models/AppSettings.cs`
- Create: `src/ClaudeTracker/Views/GitHubStarPromptWindow.xaml`
- Create: `src/ClaudeTracker/Views/GitHubStarPromptWindow.xaml.cs`
- Modify: `src/ClaudeTracker/App.xaml.cs`

**Step 1: Add settings**

In `AppSettings.cs`, add:

```csharp
[JsonPropertyName("hasStarredGitHub")]
public bool HasStarredGitHub { get; set; }

[JsonPropertyName("lastStarPromptDate")]
public DateTime? LastStarPromptDate { get; set; }

[JsonPropertyName("starPromptDismissedForever")]
public bool StarPromptDismissedForever { get; set; }
```

**Step 2: Create GitHubStarPromptWindow.xaml**

```xml
<Window x:Class="ClaudeTracker.Views.GitHubStarPromptWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True"
        Width="400" SizeToContent="Height">
    <Border CornerRadius="12" Padding="28,24"
            Background="{DynamicResource MaterialDesign.Brush.Background}"
            BorderBrush="{DynamicResource MaterialDesign.Brush.ForegroundLight}"
            BorderThickness="1">
        <Border.Effect>
            <DropShadowEffect BlurRadius="20" Opacity="0.3" ShadowDepth="4" />
        </Border.Effect>
        <StackPanel>
            <materialDesign:PackIcon Kind="StarOutline" Width="40" Height="40"
                                     Foreground="#FFC107" HorizontalAlignment="Center" Margin="0,0,0,12" />
            <TextBlock Text="Enjoying Claude Tracker?" FontSize="18" FontWeight="Bold"
                       HorizontalAlignment="Center" Margin="0,0,0,8" />
            <TextBlock TextWrapping="Wrap" TextAlignment="Center" FontSize="13" Margin="0,0,0,20"
                       Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}">
                If you find this useful, a GitHub star helps others discover it and keeps development going.
            </TextBlock>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,8">
                <Button x:Name="StarButton" Content="Star on GitHub"
                        Style="{StaticResource MaterialDesignRaisedButton}"
                        Height="36" FontSize="13" Margin="0,0,8,0" />
                <Button x:Name="RemindButton" Content="Remind Me Later"
                        Style="{StaticResource MaterialDesignOutlinedButton}"
                        Height="36" FontSize="13" />
            </StackPanel>
            <Button x:Name="DontAskButton" Content="Don't Ask Again"
                    Style="{StaticResource MaterialDesignFlatButton}"
                    HorizontalAlignment="Center" FontSize="12"
                    Foreground="{DynamicResource MaterialDesign.Brush.ForegroundLight}" />
        </StackPanel>
    </Border>
</Window>
```

**Step 3: Create GitHubStarPromptWindow.xaml.cs**

```csharp
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Views;

public partial class GitHubStarPromptWindow : Window
{
    public GitHubStarPromptWindow()
    {
        InitializeComponent();

        StarButton.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(Constants.GitHub.RepoUrl) { UseShellExecute = true });
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.HasStarredGitHub = true;
            settings.Save();
            Close();
        };

        RemindButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.LastStarPromptDate = DateTime.UtcNow;
            settings.Save();
            Close();
        };

        DontAskButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.StarPromptDismissedForever = true;
            settings.Save();
            Close();
        };
    }

    public static bool ShouldShow(Models.AppSettings settings)
    {
        if (settings.HasStarredGitHub || settings.StarPromptDismissedForever) return false;
        if (settings.FirstLaunchDate == null) return false;
        if ((DateTime.UtcNow - settings.FirstLaunchDate.Value).TotalDays < 1) return false;
        if (settings.LastStarPromptDate.HasValue &&
            (DateTime.UtcNow - settings.LastStarPromptDate.Value).TotalDays < 10) return false;
        return true;
    }
}
```

**Step 4: Trigger from App.xaml.cs**

In `OnStartup()`, after the tray icon initialization (around line 53), add:

```csharp
// Engagement prompts (delayed to not block startup)
_ = Task.Delay(5000).ContinueWith(_ =>
{
    Dispatcher.Invoke(() =>
    {
        var settings = settingsService.Settings;
        if (GitHubStarPromptWindow.ShouldShow(settings))
        {
            new GitHubStarPromptWindow().Show();
        }
    });
});
```

**Step 5: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 6: Commit**

```bash
git add src/ClaudeTracker/Models/AppSettings.cs src/ClaudeTracker/Views/GitHubStarPromptWindow.xaml src/ClaudeTracker/Views/GitHubStarPromptWindow.xaml.cs src/ClaudeTracker/App.xaml.cs
git commit -m "feat: add GitHub star prompt with 1-day delay and 10-day remind"
```

---

### Task 18: Feedback Prompt

**Files:**
- Modify: `src/ClaudeTracker/Models/AppSettings.cs`
- Modify: `src/ClaudeTracker/Utilities/Constants.cs`
- Create: `src/ClaudeTracker/Views/FeedbackPromptWindow.xaml`
- Create: `src/ClaudeTracker/Views/FeedbackPromptWindow.xaml.cs`
- Modify: `src/ClaudeTracker/App.xaml.cs`

**Step 1: Add settings + constants**

In `AppSettings.cs`:
```csharp
[JsonPropertyName("hasSentFeedback")]
public bool HasSentFeedback { get; set; }

[JsonPropertyName("lastFeedbackPromptDate")]
public DateTime? LastFeedbackPromptDate { get; set; }

[JsonPropertyName("feedbackPromptDismissedForever")]
public bool FeedbackPromptDismissedForever { get; set; }

[JsonPropertyName("feedbackRating")]
public int? FeedbackRating { get; set; }
```

In `Constants.cs`:
```csharp
public static class Feedback
{
    public const string EndpointUrl = ""; // Set when backend is ready
    public const double PromptAfterDays = 7.0;
    public const double RemindIntervalDays = 14.0;
}
```

**Step 2: Create FeedbackPromptWindow.xaml**

Material Design modal with:
- "How's your experience?" heading
- 5 clickable star icons (PackIcon Kind="Star" / "StarOutline")
- Optional TextBox for comments
- Submit (hidden when EndpointUrl empty — saves rating locally instead), Not Now, Don't Ask Again

**Step 3: Create FeedbackPromptWindow.xaml.cs**

- Star click handler toggles filled/outline and sets rating
- Submit sends `HttpClient.PostAsync` if endpoint URL is set, otherwise saves rating to settings
- `ShouldShow()` static method checks conditions (7-day delay, 14-day remind, not in same session as star prompt)

**Step 4: Wire trigger in App.xaml.cs**

In the delayed engagement task, after the star prompt check, add:
```csharp
else if (FeedbackPromptWindow.ShouldShow(settings))
{
    new FeedbackPromptWindow().Show();
}
```

The `else` ensures only one prompt per session.

**Step 5: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded.

**Step 6: Commit**

```bash
git add src/ClaudeTracker/Models/AppSettings.cs src/ClaudeTracker/Utilities/Constants.cs src/ClaudeTracker/Views/FeedbackPromptWindow.xaml src/ClaudeTracker/Views/FeedbackPromptWindow.xaml.cs src/ClaudeTracker/App.xaml.cs
git commit -m "feat: add feedback prompt with 5-star rating and optional comment"
```

---

### Task 19: Final Integration Test + Build Verification

**Files:** None new — this is verification only.

**Step 1: Run all tests**

Run: `dotnet test tests/ClaudeTracker.Tests -v normal`
Expected: All tests pass (existing + new).

**Step 2: Build Release**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj -c Release -v minimal`
Expected: Build succeeded, 0 warnings (or only pre-existing warnings).

**Step 3: Verify no regressions**

Run: `dotnet publish src/ClaudeTracker/ClaudeTracker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true --no-restore -v minimal`
Expected: Publish succeeded.

**Step 4: Commit any fixups**

If any issues found, fix and commit with message describing the fix.

**Step 5: Final commit**

```bash
git add -A
git commit -m "chore: final integration verification for gap migration v2"
```
