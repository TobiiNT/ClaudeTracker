# Dark Mode Color Refactoring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all ~130 hardcoded color hex values with 31 semantic brush tokens that adapt between light and dark themes.

**Architecture:** Single `ColorPalette.xaml` resource dictionary + `ThemeColors.cs` static class. `ApplyTheme()` replaces brush resources on theme switch (`Application.Current.Resources[key] = new SolidColorBrush(color)`) to avoid WPF frozen-brush issues. XAML uses `DynamicResource`, C# code reads from `ThemeColors` dictionaries.

**Critical note — brush freezing:** WPF auto-freezes `SolidColorBrush` objects in compiled XAML resource dictionaries. Mutating `brush.Color` in-place throws `InvalidOperationException`. Instead, `Apply()` replaces each resource with a new brush instance. `DynamicResource` bindings automatically pick up the replacement.

**Tech Stack:** WPF/.NET 8, MaterialDesignThemes 5.1.0, SkiaSharp (tray icon), CommunityToolkit.Mvvm

**Spec:** `docs/plans/2026-03-23-dark-mode-color-refactoring.md`

---

## File Structure

### New Files
- `src/ClaudeTracker/Themes/ColorPalette.xaml` — 31 SolidColorBrush resources with semantic keys
- `src/ClaudeTracker/Utilities/ThemeColors.cs` — Light/Dark color maps + `Apply(bool isDark)` + `GetHex(string key, bool isDark)` + `GetSKColor(string key, bool isDark)`

### Modified Files (grouped by task)
- `src/ClaudeTracker/App.xaml` — Add ColorPalette.xaml to merged dictionaries
- `src/ClaudeTracker/App.xaml.cs` — Call `ThemeColors.Apply(isDark)` in `ApplyTheme()`
- `src/ClaudeTracker/Themes/SharedStyles.xaml` — Replace 14 hardcoded colors with DynamicResource
- `src/ClaudeTracker/Views/PopoverWindow.xaml` — Replace ~55 hardcoded colors
- `src/ClaudeTracker/Views/FloatingUsageWindow.xaml` — Replace ~30 hardcoded colors
- `src/ClaudeTracker/Views/PermissionRequestPopup.xaml` — Replace 2 accent colors (buttons unchanged)
- `src/ClaudeTracker/Views/NotificationPopup.xaml` — Replace 1 color
- `src/ClaudeTracker/Views/SetupWizardWindow.xaml` — Replace ~9 colors
- `src/ClaudeTracker/Views/BrowserSignInWindow.xaml` — Replace 1 color
- `src/ClaudeTracker/Views/HooksOnboardingWindow.xaml` — Replace 4 colors
- `src/ClaudeTracker/Views/FeedbackPromptWindow.xaml` — Replace 2 colors
- `src/ClaudeTracker/Views/GitHubStarPromptWindow.xaml` — Replace 1 color
- `src/ClaudeTracker/Views/Settings/AppearanceView.xaml` — Replace 2 colors
- `src/ClaudeTracker/Views/Settings/GeneralSettingsView.xaml` — Replace 3 colors
- `src/ClaudeTracker/Views/Settings/HooksSettingsView.xaml` — Replace 6 colors
- `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml` — Replace 4 colors
- `src/ClaudeTracker/Views/Settings/ProfilesView.xaml` — Replace 1 color (badge text)
- `src/ClaudeTracker/Utilities/PaceStatus.cs` — Add `isDark` parameter to `GetColorHex()`
- `src/ClaudeTracker/Utilities/BrushHelper.cs` — Read colors from ThemeColors
- `src/ClaudeTracker/Models/ClaudeStatus.cs` — Add `isDark` parameter to `GetColorHex()`
- `src/ClaudeTracker/TrayIcon/TrayIconRenderer.cs` — Use ThemeColors for status/foreground
- `src/ClaudeTracker/ViewModels/PopoverViewModel.cs` — Resolve defaults from ThemeColors
- `src/ClaudeTracker/Views/PopoverWindow.xaml.cs` — Use ThemeColors for Color.FromRgb calls
- `src/ClaudeTracker/Views/FloatingUsageWindow.xaml.cs` — Use ThemeColors for Color.FromRgb calls
- `src/ClaudeTracker/Views/NotificationPopup.xaml.cs` — Use ThemeColors for Color.FromRgb calls
- `src/ClaudeTracker/Views/SetupWizardWindow.xaml.cs` — Use ThemeColors for Color.FromRgb calls
- `src/ClaudeTracker/Views/Settings/PersonalUsageView.xaml.cs` — Use ThemeColors for Color.FromRgb calls
- `src/ClaudeTracker/Views/Settings/AppearanceView.xaml.cs` — Use ThemeColors for light/dark foreground toggle
- `tests/ClaudeTracker.Tests/ClaudeStatusTests.cs` — Update for isDark parameter
- `tests/ClaudeTracker.Tests/PaceStatusTests.cs` — Update for isDark parameter

---

### Task 1: Create ColorPalette.xaml and ThemeColors.cs

**Files:**
- Create: `src/ClaudeTracker/Themes/ColorPalette.xaml`
- Create: `src/ClaudeTracker/Utilities/ThemeColors.cs`
- Modify: `src/ClaudeTracker/App.xaml:14` — Add ColorPalette to merged dictionaries
- Modify: `src/ClaudeTracker/App.xaml.cs:430-438` — Call ThemeColors.Apply in ApplyTheme

- [ ] **Step 1: Create `Themes/ColorPalette.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Text hierarchy -->
    <SolidColorBrush x:Key="TextPrimary" Color="#333333" />
    <SolidColorBrush x:Key="TextData" Color="#444444" />
    <SolidColorBrush x:Key="TextSecondary" Color="#555555" />
    <SolidColorBrush x:Key="TextTertiary" Color="#666666" />
    <SolidColorBrush x:Key="TextMuted" Color="#888888" />
    <SolidColorBrush x:Key="TextSubtle" Color="#999999" />
    <SolidColorBrush x:Key="TextFaint" Color="#BBBBBB" />

    <!-- Surfaces -->
    <SolidColorBrush x:Key="SurfacePopover" Color="#F5F5F5" />
    <SolidColorBrush x:Key="SurfaceFooter" Color="#EEEEEE" />
    <SolidColorBrush x:Key="SurfaceHover" Color="#E0E0E0" />
    <SolidColorBrush x:Key="SurfaceCard" Color="#0A000000" />
    <SolidColorBrush x:Key="SurfacePreview" Color="#333333" />

    <!-- Borders -->
    <SolidColorBrush x:Key="BorderDefault" Color="#CCCCCC" />
    <SolidColorBrush x:Key="BorderSubtle" Color="#22000000" />
    <SolidColorBrush x:Key="BorderSeparator" Color="#DDDDDD" />
    <SolidColorBrush x:Key="BorderFooter" Color="#18000000" />

    <!-- Progress -->
    <SolidColorBrush x:Key="ProgressBackground" Color="#15000000" />
    <SolidColorBrush x:Key="SessionDot" Color="#4CAF50" />

    <!-- Status (constant in light, same in dark) -->
    <SolidColorBrush x:Key="StatusSafe" Color="#4CAF50" />
    <SolidColorBrush x:Key="StatusModerate" Color="#FF9800" />
    <SolidColorBrush x:Key="StatusCritical" Color="#F44336" />

    <!-- Accents -->
    <SolidColorBrush x:Key="AccentPurple" Color="#7C4DFF" />
    <SolidColorBrush x:Key="AccentBlue" Color="#2196F3" />
    <SolidColorBrush x:Key="AccentCyan" Color="#00BCD4" />
    <SolidColorBrush x:Key="AccentAmber" Color="#FFC107" />
    <SolidColorBrush x:Key="AccentGreen" Color="#4CAF50" />
    <SolidColorBrush x:Key="AccentTeal" Color="#009688" />
    <SolidColorBrush x:Key="AccentMagenta" Color="#9C27B0" />
</ResourceDictionary>
```

- [ ] **Step 2: Create `Utilities/ThemeColors.cs`**

```csharp
using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace ClaudeTracker.Utilities;

public static class ThemeColors
{
    public static bool IsDark { get; private set; }

    private static readonly Dictionary<string, (Color Light, Color Dark)> _palette = new()
    {
        // Text
        ["TextPrimary"]   = (Color(0xFF,0x33,0x33,0x33), Color(0xFF,0xE0,0xE0,0xE0)),
        ["TextData"]      = (Color(0xFF,0x44,0x44,0x44), Color(0xFF,0xCC,0xCC,0xCC)),
        ["TextSecondary"] = (Color(0xFF,0x55,0x55,0x55), Color(0xFF,0xBB,0xBB,0xBB)),
        ["TextTertiary"]  = (Color(0xFF,0x66,0x66,0x66), Color(0xFF,0xAA,0xAA,0xAA)),
        ["TextMuted"]     = (Color(0xFF,0x88,0x88,0x88), Color(0xFF,0x88,0x88,0x88)),
        ["TextSubtle"]    = (Color(0xFF,0x99,0x99,0x99), Color(0xFF,0x77,0x77,0x77)),
        ["TextFaint"]     = (Color(0xFF,0xBB,0xBB,0xBB), Color(0xFF,0x55,0x55,0x55)),

        // Surfaces
        ["SurfacePopover"] = (Color(0xFF,0xF5,0xF5,0xF5), Color(0xFF,0x2D,0x2D,0x2D)),
        ["SurfaceFooter"]  = (Color(0xFF,0xEE,0xEE,0xEE), Color(0xFF,0x25,0x25,0x25)),
        ["SurfaceHover"]   = (Color(0xFF,0xE0,0xE0,0xE0), Color(0xFF,0x3A,0x3A,0x3A)),
        ["SurfaceCard"]    = (Color(0x0A,0x00,0x00,0x00), Color(0x0A,0xFF,0xFF,0xFF)),
        ["SurfacePreview"] = (Color(0xFF,0x33,0x33,0x33), Color(0xFF,0x1A,0x1A,0x1A)),

        // Borders
        ["BorderDefault"]   = (Color(0xFF,0xCC,0xCC,0xCC), Color(0xFF,0x44,0x44,0x44)),
        ["BorderSubtle"]    = (Color(0x22,0x00,0x00,0x00), Color(0x22,0xFF,0xFF,0xFF)),
        ["BorderSeparator"] = (Color(0xFF,0xDD,0xDD,0xDD), Color(0xFF,0x3A,0x3A,0x3A)),
        ["BorderFooter"]    = (Color(0x18,0x00,0x00,0x00), Color(0x18,0xFF,0xFF,0xFF)),

        // Progress
        ["ProgressBackground"] = (Color(0x15,0x00,0x00,0x00), Color(0x15,0xFF,0xFF,0xFF)),
        ["SessionDot"]         = (Color(0xFF,0x4C,0xAF,0x50), Color(0xFF,0x66,0xBB,0x6A)),

        // Status
        ["StatusSafe"]     = (Color(0xFF,0x4C,0xAF,0x50), Color(0xFF,0x4C,0xAF,0x50)),
        ["StatusModerate"] = (Color(0xFF,0xFF,0x98,0x00), Color(0xFF,0xFF,0x98,0x00)),
        ["StatusCritical"] = (Color(0xFF,0xF4,0x43,0x36), Color(0xFF,0xF4,0x43,0x36)),

        // Accents
        ["AccentPurple"]  = (Color(0xFF,0x7C,0x4D,0xFF), Color(0xFF,0xBB,0x86,0xFC)),
        ["AccentBlue"]    = (Color(0xFF,0x21,0x96,0xF3), Color(0xFF,0x64,0xB5,0xF6)),
        ["AccentCyan"]    = (Color(0xFF,0x00,0xBC,0xD4), Color(0xFF,0x4D,0xD0,0xE1)),
        ["AccentAmber"]   = (Color(0xFF,0xFF,0xC1,0x07), Color(0xFF,0xFF,0xC1,0x07)),
        ["AccentGreen"]   = (Color(0xFF,0x4C,0xAF,0x50), Color(0xFF,0x4C,0xAF,0x50)),
        ["AccentTeal"]    = (Color(0xFF,0x00,0x96,0x88), Color(0xFF,0x4D,0xB6,0xAC)),
        ["AccentMagenta"] = (Color(0xFF,0x9C,0x27,0xB0), Color(0xFF,0xCE,0x93,0xD8)),
    };

    private static Color Color(byte a, byte r, byte g, byte b) =>
        System.Windows.Media.Color.FromArgb(a, r, g, b);

    /// <summary>
    /// Replaces each brush resource with a new instance to avoid WPF frozen-brush errors.
    /// DynamicResource bindings automatically pick up the replacement.
    /// </summary>
    public static void Apply(bool isDark)
    {
        IsDark = isDark;
        if (Application.Current?.Resources == null) return; // Guard for test context
        foreach (var (key, (light, dark)) in _palette)
        {
            Application.Current.Resources[key] = new SolidColorBrush(isDark ? dark : light);
        }
    }

    /// <summary>Get a WPF Color for a palette key.</summary>
    public static Color Get(string key)
    {
        if (_palette.TryGetValue(key, out var pair))
            return IsDark ? pair.Dark : pair.Light;
        return Colors.Magenta; // Debug: obvious if key is wrong
    }

    /// <summary>Get a hex string for a palette key (for ViewModel binding). Opaque colors only.</summary>
    public static string GetHex(string key)
    {
        var c = Get(key);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>Get an SKColor for SkiaSharp rendering (tray icon).</summary>
    public static SKColor GetSKColor(string key)
    {
        var c = Get(key);
        return new SKColor(c.R, c.G, c.B, c.A);
    }
}
```

- [ ] **Step 3: Add ColorPalette.xaml to App.xaml merged dictionaries**

In `src/ClaudeTracker/App.xaml`, add after line 14 (`SharedStyles.xaml`):

```xml
                <ResourceDictionary Source="Themes/ColorPalette.xaml" />
```

- [ ] **Step 4: Call ThemeColors.Apply in ApplyTheme**

In `src/ClaudeTracker/App.xaml.cs`, modify `ApplyTheme()` at line 430:

```csharp
    public static void ApplyTheme(string theme)
    {
        bool isDark = theme == "dark" || (theme == "auto" && IsSystemDarkMode());

        var paletteHelper = new PaletteHelper();
        var mdTheme = paletteHelper.GetTheme();
        mdTheme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(mdTheme);

        ThemeColors.Apply(isDark);
    }
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```
feat: add ColorPalette.xaml and ThemeColors.cs for dark mode support
```

---

### Task 2: Migrate SharedStyles.xaml

**Files:**
- Modify: `src/ClaudeTracker/Themes/SharedStyles.xaml`

Replace hardcoded colors with `{DynamicResource}` tokens. Leave scrollbar alpha colors, SoftCard/SoftSeparator pairs unchanged.

- [ ] **Step 1: Remove status brush aliases (lines 5-7)**

Delete the `StatusSafeBrush`, `StatusModerateBrush`, `StatusCriticalBrush` definitions. They are aliases for `StatusSafe`, `StatusModerate`, `StatusCritical` from ColorPalette.xaml. If any XAML still references `StatusSafeBrush`, update those to `StatusSafe` etc. (Grep for `StatusSafeBrush`, `StatusModerateBrush`, `StatusCriticalBrush` to find callsites.)

- [ ] **Step 2: Replace style setter colors**

| Line | Old | New |
|------|-----|-----|
| 17 | `Value="#0A000000"` | `Value="{DynamicResource SurfaceCard}"` |
| 32 | `Value="#F5F5F5"` | `Value="{DynamicResource SurfacePopover}"` |
| 34 | `Value="#22000000"` | `Value="{DynamicResource BorderSubtle}"` |
| 45 | `Value="#EEEEEE"` | `Value="{DynamicResource SurfaceFooter}"` |
| 49 | `Value="#18000000"` | `Value="{DynamicResource BorderFooter}"` |
| 54 | `Value="#F5F5F5"` | `Value="{DynamicResource SurfacePopover}"` |
| 56 | `Value="#22000000"` | `Value="{DynamicResource BorderSubtle}"` |
| 67 | `Value="#F5F5F5"` | `Value="{DynamicResource SurfacePopover}"` |
| 68 | `Value="#CCCCCC"` | `Value="{DynamicResource BorderDefault}"` |
| 96 | `Value="#333333"` | `Value="{DynamicResource TextPrimary}"` |
| 133 | `Value="#DDDDDD"` | `Value="{DynamicResource BorderSeparator}"` |

- [ ] **Step 3: Replace ControlTemplate trigger color (line 120)**

```xml
<!-- Before -->
<Setter TargetName="Bd" Property="Background" Value="#E0E0E0" />

<!-- After -->
<Setter TargetName="Bd" Property="Background" Value="{DynamicResource SurfaceHover}" />
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`

- [ ] **Step 5: Commit**

```
refactor: migrate SharedStyles.xaml to semantic color tokens
```

---

### Task 3: Migrate PopoverWindow.xaml

**Files:**
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml`

This is the largest file — ~55 color references. Use find-and-replace with these mappings:

- [ ] **Step 1: Replace all text color hex values**

| Old | New Token | Count |
|-----|-----------|-------|
| `Foreground="#333"` | `Foreground="{DynamicResource TextPrimary}"` | 1 |
| `Foreground="#444"` | `Foreground="{DynamicResource TextData}"` | 8 |
| `Foreground="#555"` | `Foreground="{DynamicResource TextSecondary}"` | 3 |
| `Foreground="#666"` | `Foreground="{DynamicResource TextTertiary}"` | 8 |
| `Foreground="#777"` | `Foreground="{DynamicResource TextMuted}"` | 3 (lines 75, 370, 378, 384) |
| `Foreground="#888"` | `Foreground="{DynamicResource TextMuted}"` | 8 |
| `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` | 14 |

- [ ] **Step 2: Replace progress bar and status colors**

| Line | Old | New |
|------|-----|-----|
| 103, 133, 167, 179 | `Background="#15000000"` | `Background="{DynamicResource ProgressBackground}"` |
| 104, 134 | `Background="#4CAF50"` | `Background="{DynamicResource AccentGreen}"` |
| 168 | `Background="#7C4DFF"` | `Background="{DynamicResource AccentPurple}"` |
| 180 | `Background="#00BCD4"` | `Background="{DynamicResource AccentCyan}"` |
| 289 | `Fill="#4CAF50"` | `Fill="{DynamicResource SessionDot}"` |
| 264 | `Background="#08000000"` | `Background="{DynamicResource SurfaceCard}"` |

- [ ] **Step 3: Leave these unchanged**

- Line 81: `#10FF9800` (alpha-based status panel tint — works in both themes)

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`

- [ ] **Step 5: Commit**

```
refactor: migrate PopoverWindow.xaml to semantic color tokens
```

---

### Task 4: Migrate FloatingUsageWindow.xaml

**Files:**
- Modify: `src/ClaudeTracker/Views/FloatingUsageWindow.xaml`

- [ ] **Step 1: Replace surface/border colors**

| Line | Old | New |
|------|-----|-----|
| 16 | `Background="#F5F5F5"` | `Background="{DynamicResource SurfacePopover}"` |
| 22 | `Background="#F5F5F5"` | `Background="{DynamicResource SurfacePopover}"` |
| 22 | `BorderBrush="#22000000"` | `BorderBrush="{DynamicResource BorderSubtle}"` |

- [ ] **Step 2: Replace text colors**

Same mapping as Task 3:
- `#333` → `TextPrimary` (lines 68, 116)
- `#444` → `TextData` (lines 92, 136, 151)
- `#666` → `TextTertiary` (lines 66, 90, 114, 127)
- `#888` → `TextMuted` (lines 35, 38, 131, 146)
- `#999` → `TextSubtle` (lines 45, 52, 58, 75, 80, 99, 104, 122, 138, 153)
- `#BBB` → `TextFaint` (line 162)

- [ ] **Step 3: Replace progress/accent colors**

| Line | Old | New |
|------|-----|-----|
| 70, 94, 118 | `Background="#15000000"` | `Background="{DynamicResource ProgressBackground}"` |
| 71, 95 | `Background="#4CAF50"` | `Background="{DynamicResource AccentGreen}"` |
| 119 | `Background="#2196F3"` | `Background="{DynamicResource AccentBlue}"` |

- [ ] **Step 4: Build and commit**

```
refactor: migrate FloatingUsageWindow.xaml to semantic color tokens
```

---

### Task 5: Migrate remaining XAML files (11 files)

**Files:**
- Modify: All remaining XAML files listed below

These files have fewer color references (1-9 each). Migrate them all in one task.

- [ ] **Step 1: PermissionRequestPopup.xaml**

| Line | Old | New |
|------|-----|-----|
| 38 | `Foreground="#BB86FC"` | `Foreground="{DynamicResource AccentPurple}"` |

Leave unchanged: diff labels (#FF8A80, #69F0AE), buttons (#388E3C, #D32F2F, etc.), disabled state (#2E5930, #66FFFFFF)

- [ ] **Step 2: NotificationPopup.xaml**

| Line | Old | New |
|------|-----|-----|
| 29 | `Foreground="#FF9800"` | `Foreground="{DynamicResource StatusModerate}"` |

- [ ] **Step 3: SetupWizardWindow.xaml**

| Line | Old | New |
|------|-----|-----|
| 40, 62, 110, 133 | `Foreground="#888"` | `Foreground="{DynamicResource TextMuted}"` |
| 47 | `Foreground="#7C4DFF"` | `Foreground="{DynamicResource AccentPurple}"` |
| 51, 72 | `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` |
| 68 | `Foreground="#2196F3"` | `Foreground="{DynamicResource AccentBlue}"` |
| 148 | `Foreground="#4CAF50"` | `Foreground="{DynamicResource StatusSafe}"` |

- [ ] **Step 4: BrowserSignInWindow.xaml**

| Line | Old | New |
|------|-----|-----|
| 16 | `Foreground="#888"` | `Foreground="{DynamicResource TextMuted}"` |

- [ ] **Step 5: HooksOnboardingWindow.xaml**

| Line | Old | New |
|------|-----|-----|
| 33 | `Foreground="#7C4DFF"` | `Foreground="{DynamicResource AccentPurple}"` |
| 43 | `Foreground="#2196F3"` | `Foreground="{DynamicResource AccentBlue}"` |
| 53, 75 | `Foreground="#4CAF50"` | `Foreground="{DynamicResource StatusSafe}"` |

- [ ] **Step 6: FeedbackPromptWindow.xaml**

| Line | Old | New |
|------|-----|-----|
| 18 | `Foreground="#2196F3"` | `Foreground="{DynamicResource AccentBlue}"` |
| 29-42 | `Foreground="#FFC107"` | `Foreground="{DynamicResource AccentAmber}"` |

- [ ] **Step 7: GitHubStarPromptWindow.xaml**

| Line | Old | New |
|------|-----|-----|
| 18 | `Foreground="#FFC107"` | `Foreground="{DynamicResource AccentAmber}"` |

- [ ] **Step 8: Settings/AppearanceView.xaml**

| Line | Old | New |
|------|-----|-----|
| 17 | `Background="#333"` | `Background="{DynamicResource SurfacePreview}"` |
| 22 | `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` |

- [ ] **Step 9: Settings/GeneralSettingsView.xaml**

| Line | Old | New |
|------|-----|-----|
| 33, 42, 51 | `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` |

- [ ] **Step 10: Settings/HooksSettingsView.xaml**

| Line | Old | New |
|------|-----|-----|
| 17, 35, 48, 67, 85, 117 | `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` |

- [ ] **Step 11: Settings/PersonalUsageView.xaml**

| Line | Old | New |
|------|-----|-----|
| 85 | `Foreground="#999"` | `Foreground="{DynamicResource TextSubtle}"` |
| 123, 132, 146 | `Foreground="#888"` | `Foreground="{DynamicResource TextMuted}"` |

- [ ] **Step 12: Settings/ProfilesView.xaml**

| Line | Old | New |
|------|-----|-----|
| 49 | `Foreground="#4CAF50"` | `Foreground="{DynamicResource StatusSafe}"` |

Leave unchanged: line 46 `#1B4CAF50` (alpha-based badge bg — works in both themes)

- [ ] **Step 13: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`

- [ ] **Step 14: Commit**

```
refactor: migrate remaining XAML files to semantic color tokens
```

---

### Task 6: Migrate C# color code

**Files:**
- Modify: `src/ClaudeTracker/Utilities/PaceStatus.cs:96-108`
- Modify: `src/ClaudeTracker/Models/ClaudeStatus.cs:17-25`
- Modify: `src/ClaudeTracker/Utilities/BrushHelper.cs:18-28`
- Modify: `src/ClaudeTracker/TrayIcon/TrayIconRenderer.cs:282-298`
- Modify: `src/ClaudeTracker/ViewModels/PopoverViewModel.cs:57-70`
- Modify: `src/ClaudeTracker/Views/PopoverWindow.xaml.cs:215-238`
- Modify: `src/ClaudeTracker/Views/FloatingUsageWindow.xaml.cs:197-217`

- [ ] **Step 1: Update PaceStatus.GetColorHex**

Replace `GetColorHex(PaceStatus status)` at lines 96-108:

```csharp
public static string GetColorHex(PaceStatus status)
{
    return status switch
    {
        PaceStatus.Comfortable => ThemeColors.GetHex("StatusSafe"),
        PaceStatus.OnTrack     => ThemeColors.GetHex("AccentTeal"),
        PaceStatus.Warming     => ThemeColors.GetHex("AccentAmber"),
        PaceStatus.Pressing    => ThemeColors.GetHex("StatusModerate"),
        PaceStatus.Critical    => ThemeColors.GetHex("StatusCritical"),
        PaceStatus.Runaway     => ThemeColors.GetHex("AccentMagenta"),
        _                      => ThemeColors.GetHex("StatusSafe")
    };
}
```

Add `using ClaudeTracker.Utilities;` if not already present.

- [ ] **Step 2: Update ClaudeStatus.GetColorHex**

Replace `GetColorHex(StatusIndicator indicator)` at lines 17-25:

```csharp
public static string GetColorHex(StatusIndicator indicator) => indicator switch
{
    StatusIndicator.None        => ThemeColors.GetHex("StatusSafe"),
    StatusIndicator.Minor       => ThemeColors.GetHex("AccentAmber"),
    StatusIndicator.Major       => ThemeColors.GetHex("StatusModerate"),
    StatusIndicator.Critical    => ThemeColors.GetHex("StatusCritical"),
    StatusIndicator.Unknown     => ThemeColors.GetHex("TextMuted"),
    _                           => ThemeColors.GetHex("TextMuted")
};
```

Add `using ClaudeTracker.Utilities;` if not already present.

- [ ] **Step 3: Update BrushHelper**

Replace hardcoded `Color.FromRgb` calls at lines 18-28 with `ThemeColors.Get()`:

```csharp
// Replace the fallback and status color methods to use ThemeColors
public static SolidColorBrush GetStatusBrush(UsageStatusLevel status)
{
    var color = status switch
    {
        UsageStatusLevel.Safe     => ThemeColors.Get("StatusSafe"),
        UsageStatusLevel.Moderate => ThemeColors.Get("StatusModerate"),
        UsageStatusLevel.Critical => ThemeColors.Get("StatusCritical"),
        _                         => ThemeColors.Get("StatusSafe")
    };
    return new SolidColorBrush(color);
}
```

- [ ] **Step 4: Update TrayIconRenderer status/foreground colors**

Replace `GetStatusColor` at line 282 and `GetForegroundColor` at line 296:

```csharp
private static SKColor GetStatusColor(UsageStatusLevel status, bool monochrome, bool isDarkMode)
{
    if (monochrome)
        return isDarkMode ? SKColors.White : SKColors.Black;

    return status switch
    {
        UsageStatusLevel.Safe     => ThemeColors.GetSKColor("StatusSafe"),
        UsageStatusLevel.Moderate => ThemeColors.GetSKColor("StatusModerate"),
        UsageStatusLevel.Critical => ThemeColors.GetSKColor("StatusCritical"),
        _                         => ThemeColors.GetSKColor("StatusSafe")
    };
}

private static SKColor GetForegroundColor(bool isDarkMode)
{
    return isDarkMode ? SKColors.White : new SKColor(0x33, 0x33, 0x33);
}
```

- [ ] **Step 5: Update PopoverViewModel defaults**

At lines 57, 64, 70 — these are initial values. They'll be overwritten on first refresh, but set them correctly:

```csharp
[ObservableProperty] private string _sessionPaceColorHex = "#4CAF50";
[ObservableProperty] private string _weeklyPaceColorHex = "#4CAF50";
[ObservableProperty] private string _claudeStatusColorHex = "#888888";
```

Note: The #9E9E9E → #888888 change for `_claudeStatusColorHex` aligns with `TextMuted` token.

- [ ] **Step 6: Update PopoverWindow.xaml.cs Color.FromRgb calls**

Replace hardcoded Color.FromRgb at lines ~215-238 with ThemeColors.Get():

```csharp
// Replace each Color.FromRgb(0xNN, 0xNN, 0xNN) with:
ThemeColors.Get("StatusModerate")   // was Color.FromRgb(0xFF, 0x98, 0x00)
ThemeColors.Get("AccentBlue")       // was Color.FromRgb(0x21, 0x96, 0xF3)
ThemeColors.Get("TextMuted")        // was Color.FromRgb(0x99, 0x99, 0x99)
ThemeColors.Get("StatusSafe")       // was Color.FromRgb(0x4C, 0xAF, 0x50)
```

- [ ] **Step 7: Update FloatingUsageWindow.xaml.cs Color.FromRgb calls**

Same pattern as Step 6, replace at lines ~197-217:

```csharp
ThemeColors.Get("StatusModerate")   // was Color.FromRgb(0xFF, 0x98, 0x00)
ThemeColors.Get("AccentBlue")       // was Color.FromRgb(0x21, 0x96, 0xF3)
ThemeColors.Get("TextMuted")        // was Color.FromRgb(0x99, 0x99, 0x99)
ThemeColors.Get("StatusSafe")       // was Color.FromRgb(0x4C, 0xAF, 0x50)
```

- [ ] **Step 8: Update remaining code-behind files**

Apply the same `Color.FromRgb` → `ThemeColors.Get()` pattern to these files:

- **`NotificationPopup.xaml.cs`**: Replace Color.FromRgb calls for blue/orange/red notification icon colors with `ThemeColors.Get("AccentBlue")`, `ThemeColors.Get("StatusModerate")`, `ThemeColors.Get("StatusCritical")`
- **`SetupWizardWindow.xaml.cs`**: Replace Color.FromRgb calls for green/gray/red/orange step indicator colors with `ThemeColors.Get("StatusSafe")`, `ThemeColors.Get("TextMuted")`, `ThemeColors.Get("StatusCritical")`, `ThemeColors.Get("StatusModerate")`
- **`PersonalUsageView.xaml.cs`**: Replace Color.FromRgb calls for connected/disconnected status colors with `ThemeColors.Get("StatusSafe")`, `ThemeColors.Get("TextMuted")`, `ThemeColors.Get("StatusCritical")`, `ThemeColors.Get("StatusModerate")`
- **`AppearanceView.xaml.cs`**: Replace manual dark/light foreground toggle (lines ~289-290 `Color.FromRgb(0x33,0x33,0x33)` / `Color.FromRgb(0xEE,0xEE,0xEE)`) with `ThemeColors.Get("TextPrimary")` / `ThemeColors.Get("SurfaceFooter")`. Leave gradient stop colors in icon preview unchanged (intentionally fixed for preview rendering).

Add `using ClaudeTracker.Utilities;` to each file if not already present.

- [ ] **Step 9: Build to verify**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj --configuration Release`

- [ ] **Step 10: Commit**

```
refactor: migrate C# hardcoded colors to ThemeColors
```

---

### Task 7: Update tests

**Files:**
- Modify: `tests/ClaudeTracker.Tests/ClaudeStatusTests.cs:25-32`
- Modify: `tests/ClaudeTracker.Tests/PaceStatusTests.cs:55-62`

- [ ] **Step 1: Update ClaudeStatusTests**

The test uses `ClaudeStatus.GetColorHex()` which now reads from `ThemeColors`. Since tests run without WPF Application context, `ThemeColors.IsDark` defaults to `false` (light mode). Update expected values:

```csharp
[Theory]
[InlineData(StatusIndicator.None, "#4CAF50")]
[InlineData(StatusIndicator.Minor, "#FFC107")]
[InlineData(StatusIndicator.Major, "#FF9800")]
[InlineData(StatusIndicator.Critical, "#F44336")]
[InlineData(StatusIndicator.Unknown, "#888888")]  // was #9E9E9E, now TextMuted
public void GetColorHex_ReturnsCorrectColor(StatusIndicator indicator, string expectedHex)
{
    Assert.Equal(expectedHex, ClaudeStatus.GetColorHex(indicator));
}
```

- [ ] **Step 2: Update PaceStatusTests**

Same pattern — light mode defaults apply:

```csharp
public void GetColorHex_ReturnsCorrectHex()
{
    Assert.Equal("#4CAF50", PaceStatusCalculator.GetColorHex(PaceStatus.Comfortable));
    Assert.Equal("#009688", PaceStatusCalculator.GetColorHex(PaceStatus.OnTrack));
    Assert.Equal("#FFC107", PaceStatusCalculator.GetColorHex(PaceStatus.Warming));
    Assert.Equal("#FF9800", PaceStatusCalculator.GetColorHex(PaceStatus.Pressing));
    Assert.Equal("#F44336", PaceStatusCalculator.GetColorHex(PaceStatus.Critical));
    Assert.Equal("#9C27B0", PaceStatusCalculator.GetColorHex(PaceStatus.Runaway));
}
```

These should still pass since `ThemeColors.IsDark` defaults to `false` and the `_hexExtras` dictionary returns light values.

- [ ] **Step 3: Run all tests**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 4: Commit**

```
test: update color assertions for ThemeColors integration
```

---

### Task 8: Verify no remaining hardcoded colors

- [ ] **Step 1: Grep for remaining hardcoded hex in XAML**

Run a grep for hex color patterns in XAML files. Expected remaining are only:
- Scrollbar thumbs: `#40888888`, `#60AAAAAA`, `#80AAAAAA`
- SoftCard/SoftSeparator pairs: `#E8FFFFFF`, `#E8202020`, `#1A000000`, `#1AFFFFFF`
- Status panel tint: `#10FF9800`
- Active badge bg: `#1B4CAF50`
- Session item bg: `#08000000`
- Permission popup buttons/diff: `#388E3C`, `#43A047`, `#2E5930`, `#66FFFFFF`, `#D32F2F`, `#E53935`, `#FF8A80`, `#69F0AE`
- Color swatch border: `#44000000`
- AppearanceView hint text: `FF9800` (no `#`, inside hint text string)

Any other hex color found is a missed migration.

- [ ] **Step 2: Run the app, toggle theme in Settings → Appearance**

Verify:
- Popover updates live (text, backgrounds, borders, progress bars)
- Floating widget updates live
- Context menu uses correct colors
- Notification popups use correct colors
- Settings tabs all render correctly in both themes
- Tray icon renders correctly

- [ ] **Step 3: Final commit**

```
chore: verify dark mode color migration complete
```
