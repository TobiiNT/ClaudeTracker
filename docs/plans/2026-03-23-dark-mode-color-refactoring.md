# Dark Mode Color Refactoring

**Date**: 2026-03-23
**Status**: Approved

## Summary

Refactor all ~130 hardcoded color hex values across 20+ files into a single adaptive semantic color palette. The palette defines 31 named brush tokens that swap between light and dark values when the theme changes, using the same in-place update pattern MaterialDesign already uses.

## Problem

All custom UI colors are hardcoded for light mode only. MaterialDesign's `DynamicResource` brushes (`MaterialDesign.Brush.Foreground`, etc.) adapt automatically on theme switch, but the ~42 unique hex colors used in popover, floating widget, notification, context menu, and settings views do not respond to dark mode at all.

## Approach: Single Adaptive Palette

One `ColorPalette.xaml` resource dictionary defines semantic brush names. On theme switch, `ApplyTheme()` updates each brush's `Color` property in-place. No duplicate resource dictionaries — same keys, swapped values.

### Why This Approach

- Already how MaterialDesign works in this app — consistent pattern
- Single set of resource keys — no light/dark file duplication to maintain
- `DynamicResource` bindings update live — no window re-creation needed
- Minimal code change — just extend existing `ApplyTheme()` method

## Semantic Token Palette

### Text (7 tokens)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `TextPrimary` | #333333 | #E0E0E0 | Main text, headings |
| `TextData` | #444444 | #CCCCCC | Data values (percentages, costs, model counts) — 10 uses in Popover/Floating |
| `TextSecondary` | #555555 | #BBBBBB | Project names, activity detail |
| `TextTertiary` | #666666 | #AAAAAA | Labels ("Session", "Weekly", "Overage") |
| `TextMuted` | #888888 | #888888 | Icons, step labels, "Today" text |
| `TextSubtle` | #999999 | #777777 | Hints, reset times, pace, descriptions |
| `TextFaint` | #BBBBBB | #555555 | "Last updated" timestamp |

### Surfaces (5 tokens)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `SurfacePopover` | #F5F5F5 | #2D2D2D | Popover, floating widget, context menu bg |
| `SurfaceFooter` | #EEEEEE | #252525 | Popover footer bar |
| `SurfaceHover` | #E0E0E0 | #3A3A3A | Menu item hover state |
| `SurfaceCard` | #0A000000 | #0AFFFFFF | Usage card background tint |
| `SurfacePreview` | #333333 | #1A1A1A | Icon preview bg (Appearance settings) |

### Borders (4 tokens)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `BorderDefault` | #CCCCCC | #444444 | Context menu border |
| `BorderSubtle` | #22000000 | #22FFFFFF | Popover/floating widget border, section separators |
| `BorderSeparator` | #DDDDDD | #3A3A3A | Menu separators |
| `BorderFooter` | #18000000 | #18FFFFFF | Footer top border |

### Progress & Data (2 tokens)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `ProgressBackground` | #15000000 | #15FFFFFF | Progress bar track |
| `SessionDot` | #4CAF50 | #66BB6A | Active session indicator dot |

### Status (3 tokens — constant across themes)

| Token | Value | Usage |
|-------|-------|-------|
| `StatusSafe` | #4CAF50 | Safe usage, success states, check icons |
| `StatusModerate` | #FF9800 | Moderate usage, warnings |
| `StatusCritical` | #F44336 | Critical usage, deny button |

### Accents (7 tokens)

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `AccentPurple` | #7C4DFF | #BB86FC | Opus model, console icon, shield, tool names |
| `AccentBlue` | #2196F3 | #64B5F6 | API progress, web icon, info elements |
| `AccentCyan` | #00BCD4 | #4DD0E1 | Sonnet model progress |
| `AccentAmber` | #FFC107 | #FFC107 | Star icons, minor status |
| `AccentGreen` | #4CAF50 | #4CAF50 | Session progress fill, onboarding icons |
| `AccentTeal` | #009688 | #4DB6AC | Pace "OnTrack" status (PaceStatus.cs) |
| `AccentMagenta` | #9C27B0 | #CE93D8 | Pace "Runaway" status (PaceStatus.cs) |

### Claude Status Indicator (reuses status + accent tokens)

| Status | Token |
|--------|-------|
| None/Operational | `StatusSafe` |
| Minor | `AccentAmber` |
| Major | `StatusModerate` |
| Critical | `StatusCritical` |
| Unknown | `TextMuted` (#888 — intentional change from #9E9E9E for consistency) |

**Total: 31 semantic tokens** replacing 45+ unique hardcoded hex values across ~130 callsites.

## Not Tokenized (Unchanged)

These colors work correctly in both themes without changes:

- **Scrollbar thumbs**: `#40888888`, `#60AAAAAA`, `#80AAAAAA` — alpha-based, adapt naturally
- **Status panel tint**: `#10FF9800` — alpha-based orange overlay
- **Active badge bg**: `#1B4CAF50` — alpha-based green tint
- **Session item bg**: `#08000000` → keep as `SurfaceCard` (already tokenized above) or leave as alpha
- **Disabled text**: `#66FFFFFF` — always on colored button bg
- **Diff labels**: `#FF8A80`, `#69F0AE` — always on dark permission popup
- **Permission buttons**: Allow (#388E3C, #43A047) and Deny (#D32F2F, #E53935) — always colored bg
- **Frosted cards**: `SoftCardBackground` / `SoftCardBackgroundDark` — already have both variants, selected by existing theme logic
- **Soft separators**: `SoftSeparator` / `SoftSeparatorDark` (#1A000000 / #1AFFFFFF) — same pattern as frosted cards
- **Color swatch border**: `#44000000` in AppearanceView — alpha-based, low-risk; tokenize as `BorderSubtle` if visibility is poor on dark

## Components

### 1. `Themes/ColorPalette.xaml` (new)

Resource dictionary defining all 27 `SolidColorBrush` resources with light-mode defaults:

```xml
<ResourceDictionary>
    <!-- Text -->
    <SolidColorBrush x:Key="TextPrimary" Color="#333333" />
    <SolidColorBrush x:Key="TextSecondary" Color="#555555" />
    <!-- ... all 27 tokens ... -->
</ResourceDictionary>
```

Added to `App.xaml` merged dictionaries alongside existing `SharedStyles.xaml`.

### 2. `Utilities/ThemeColors.cs` (new)

Static class containing light and dark color maps:

```csharp
public static class ThemeColors
{
    public static readonly Dictionary<string, Color> Light = new()
    {
        ["TextPrimary"] = (Color)ColorConverter.ConvertFromString("#333333"),
        ["TextSecondary"] = (Color)ColorConverter.ConvertFromString("#555555"),
        // ...
    };

    public static readonly Dictionary<string, Color> Dark = new()
    {
        ["TextPrimary"] = (Color)ColorConverter.ConvertFromString("#E0E0E0"),
        ["TextSecondary"] = (Color)ColorConverter.ConvertFromString("#BBBBBB"),
        // ...
    };

    public static void Apply(bool isDark)
    {
        var palette = isDark ? Dark : Light;
        foreach (var (key, color) in palette)
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush)
                brush.Color = color;
        }
    }
}
```

### 3. `App.ApplyTheme()` extension

After MaterialDesign theme switch, call `ThemeColors.Apply(isDark)`:

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

### 4. XAML Migration

Replace hardcoded hex values with `{DynamicResource TokenName}` across all XAML files. Example:

```xml
<!-- Before -->
<TextBlock Foreground="#333" Text="{Binding Percent}" />
<ProgressBar Background="#15000000" />

<!-- After -->
<TextBlock Foreground="{DynamicResource TextPrimary}" Text="{Binding Percent}" />
<ProgressBar Background="{DynamicResource ProgressBackground}" />
```

**Files to migrate** (15 XAML files):
- `Themes/SharedStyles.xaml` — style setters
- `Views/PopoverWindow.xaml` — ~45 color references
- `Views/FloatingUsageWindow.xaml` — ~25 color references
- `Views/PermissionRequestPopup.xaml` — accent colors only (buttons unchanged)
- `Views/NotificationPopup.xaml` — warning icon color
- `Views/SetupWizardWindow.xaml` — accent/step colors
- `Views/BrowserSignInWindow.xaml` — status text
- `Views/HooksOnboardingWindow.xaml` — accent icons
- `Views/FeedbackPromptWindow.xaml` — accent icons
- `Views/GitHubStarPromptWindow.xaml` — star icon
- `Views/Settings/AppearanceView.xaml` — preview bg, hints
- `Views/Settings/GeneralSettingsView.xaml` — description text
- `Views/Settings/HooksSettingsView.xaml` — description text
- `Views/Settings/PersonalUsageView.xaml` — connected icons, hints
- `Views/Settings/ProfilesView.xaml` — active badge

### 5. C# Migration (7 files)

**WPF Brush-based** (read from `ThemeColors` or WPF resource dictionary):

- **`BrushHelper.cs`**: Read status colors from `ThemeColors` dictionaries instead of hardcoded RGB
- **`ClaudeStatus.cs`**: `GetColorHex()` accepts `isDark` parameter; maps Unknown to `TextMuted` (#888) instead of #9E9E9E
- **`FloatingUsageWindow.xaml.cs`**: Code-behind `Color.FromRgb()` calls (lines 197-217) use resource lookup
- **`PopoverWindow.xaml.cs`**: `BrushFromHex()` calls for pace dots/text already work (hex comes from ViewModel)

**SkiaSharp-based** (cannot use WPF resources — needs `ThemeColors` static lookup):

- **`TrayIconRenderer.cs`**: Already has `isDark` parameter. Add `ThemeColors.GetSKColor(key, isDark)` helper to convert `System.Windows.Media.Color` → `SKColor` for status/foreground colors
- **`TrayIconManager.cs`**: Tooltip uses `Brushes.White` — keep as-is (tooltip always dark bg)

**ViewModel-bound colors** (cannot use `DynamicResource` — bound via data binding):

- **`PaceStatus.cs`**: `GetColorHex(PaceStatus, bool isDark)` — add isDark parameter. Maps to `AccentTeal` (#009688/#4DB6AC), `AccentMagenta` (#9C27B0/#CE93D8), and existing status tokens
- **`PopoverViewModel.cs`**: Default color hex values (`SessionPaceColorHex`, `WeeklyPaceColorHex`, `ClaudeStatusColorHex`) resolve from `ThemeColors` based on current theme. `RefreshData()` passes `isDark` to `PaceStatusCalculator.GetColorHex()` and `ClaudeStatus.GetColorHex()`

### 6. `SharedStyles.xaml` Update

Migrate inline colors in styles to use palette tokens:

```xml
<!-- Before -->
<Setter Property="Background" Value="#F5F5F5" />

<!-- After -->
<Setter Property="Background" Value="{DynamicResource SurfacePopover}" />
```

Status brushes (`StatusSafeBrush`, `StatusModerateBrush`, `StatusCriticalBrush`) already defined here — keep them but source their colors from the palette.

### 7. ControlTemplate Trigger Setters

Trigger setters in `ControlTemplate` (e.g., hover state at SharedStyles.xaml line 120) must also use `{DynamicResource SurfaceHover}` — not just top-level style setters. WPF freezes inline brushes in templates, but `DynamicResource` replaces the reference on theme switch, which works correctly.

### 8. Hex-to-Token Mapping Reference

| Hex Value(s) | Token |
|--------------|-------|
| `#333`, `#333333` | `TextPrimary` |
| `#444`, `#444444` | `TextData` |
| `#555`, `#555555` | `TextSecondary` |
| `#666`, `#666666` | `TextTertiary` |
| `#777`, `#777777` | `TextMuted` (or `TextSubtle` context-dependent) |
| `#888`, `#888888` | `TextMuted` |
| `#999`, `#999999` | `TextSubtle` |
| `#BBB`, `#BBBBBB` | `TextFaint` |
| `#F5F5F5` | `SurfacePopover` |
| `#EEEEEE` | `SurfaceFooter` |
| `#E0E0E0` | `SurfaceHover` |
| `#0A000000` | `SurfaceCard` |
| `#CCCCCC` | `BorderDefault` |
| `#22000000` | `BorderSubtle` |
| `#DDDDDD` | `BorderSeparator` |
| `#18000000` | `BorderFooter` |
| `#15000000` | `ProgressBackground` |
| `#4CAF50` (progress/dots) | `AccentGreen` / `StatusSafe` / `SessionDot` |
| `#FF9800` | `StatusModerate` |
| `#F44336` | `StatusCritical` |
| `#FFC107` | `AccentAmber` |
| `#7C4DFF` | `AccentPurple` |
| `#BB86FC` | `AccentPurple` (dark value) |
| `#2196F3` | `AccentBlue` |
| `#00BCD4` | `AccentCyan` |
| `#009688` | `AccentTeal` |
| `#9C27B0` | `AccentMagenta` |
| `#9E9E9E` | `TextMuted` (intentional merge) |

## Testing

- Toggle theme (light/dark/auto) in Settings → Appearance and verify all surfaces update live
- Check popover, floating widget, notification, context menu, settings tabs in both themes
- Verify tray icon renders correctly in both taskbar dark/light modes
- Confirm no hardcoded hex values remain in XAML (grep for `#[0-9A-Fa-f]` patterns in `.xaml` files)
- Run existing unit tests — `ClaudeStatusTests` and `PaceStatusTests` need updates for `isDark` parameter
- Verify pace dot/text colors update when switching theme while popover is open

## Migration Safety

- All changes are visual only — no business logic affected
- `DynamicResource` is forward-compatible — if a key is missing, WPF falls back gracefully
- Existing `SoftCardBackground`/`SoftCardBackgroundDark` selection logic stays unchanged
- Alpha-based colors left in place — zero risk of regression
