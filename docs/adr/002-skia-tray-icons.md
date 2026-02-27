# ADR 002: SkiaSharp for Tray Icons

**Status:** Accepted
**Date:** 2025-01-15

## Context

Windows tray icons need to be rendered at various DPI scales (100%, 125%, 150%, 200%) and in multiple visual styles (battery, ring, progress bar, percentage text, compact dot). GDI+ is the traditional approach but produces blurry results at non-100% DPI and has limited drawing primitives.

## Decision

Use SkiaSharp to render tray icons as resolution-independent bitmaps, then convert to `System.Drawing.Icon` for the Hardcodet TaskbarIcon control.

- `TrayIconRenderer` queries `GetDpiForSystem()` and renders at the appropriate pixel size
- Five icon styles are supported, each drawn with anti-aliased SkiaSharp primitives
- Status colors (green/orange/red) are applied based on `UsageStatusLevel`
- Preview images for the Settings UI use the same renderer at a larger size

## Consequences

- **Positive:** Crisp icons at all DPI scales; consistent rendering across Windows versions
- **Positive:** Single rendering pipeline for both tray icons and settings previews
- **Negative:** SkiaSharp adds ~8MB to the application size (native binaries)
- **Negative:** Bitmap-to-Icon conversion requires GDI handle management (`GetHicon()` / `Clone()`)
