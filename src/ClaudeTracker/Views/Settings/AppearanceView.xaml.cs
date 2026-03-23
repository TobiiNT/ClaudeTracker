using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.TrayIcon;
using ClaudeTracker.Utilities;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class AppearanceView : UserControl
{
    private readonly AppearanceViewModel _vm;
    private readonly TrayIconRenderer _renderer;

    // "auto" = status-based colors, "mono" = monochrome, otherwise hex
    private string _selectedColorMode;

    private static readonly (string Hex, string Label)[] PresetColors =
    [
        ("auto", "Auto"),
        ("mono", "Mono"),
        ("2196F3", ""),    // Blue
        ("7C4DFF", ""),    // Purple
        ("FF9800", ""),    // Orange
        ("F44336", ""),    // Red
        ("00BCD4", ""),    // Cyan
        ("E91E63", ""),    // Pink
        ("607D8B", ""),    // Blue Grey
        ("795548", ""),    // Brown
    ];

    public AppearanceView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<AppearanceViewModel>();
        _renderer = App.Services.GetRequiredService<TrayIconRenderer>();
        DataContext = _vm;

        // Determine initial color mode
        if (_vm.MonochromeMode)
            _selectedColorMode = "mono";
        else if (_vm.UseCustomColor && !string.IsNullOrEmpty(_vm.CustomColorHex))
            _selectedColorMode = _vm.CustomColorHex;
        else
            _selectedColorMode = "auto";

        // Set initial icon style
        SetIconStyleRadio(_vm.SessionIconStyle);

        StyleBattery.Checked += (_, _) => { _vm.SessionIconStyle = MenuBarIconStyle.Battery; UpdatePreview(); };
        StyleBar.Checked += (_, _) => { _vm.SessionIconStyle = MenuBarIconStyle.ProgressBar; UpdatePreview(); };
        StylePercent.Checked += (_, _) => { _vm.SessionIconStyle = MenuBarIconStyle.Percentage; UpdatePreview(); };
        StyleRing.Checked += (_, _) => { _vm.SessionIconStyle = MenuBarIconStyle.Ring; UpdatePreview(); };
        StyleCompact.Checked += (_, _) => { _vm.SessionIconStyle = MenuBarIconStyle.Compact; UpdatePreview(); };

        // Theme
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (item.Tag?.ToString() == _vm.Theme)
            {
                ThemeCombo.SelectedItem = item;
                break;
            }
        }
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item)
            {
                _vm.Theme = item.Tag?.ToString() ?? "auto";
                UpdatePreview();
            }
        };

        // Remaining toggle
        RemainingToggle.IsChecked = _vm.ShowRemainingPercentage;
        RemainingToggle.Checked += (_, _) => { _vm.ShowRemainingPercentage = true; UpdatePreview(); };
        RemainingToggle.Unchecked += (_, _) => { _vm.ShowRemainingPercentage = false; UpdatePreview(); };

        // Color picker
        BuildColorSwatches();
        HexColorInput.Text = (_selectedColorMode != "auto" && _selectedColorMode != "mono")
            ? _selectedColorMode : "";
        UpdateColorPreviewSwatch(_selectedColorMode);
        HexColorInput.TextChanged += (_, _) =>
        {
            var hex = HexColorInput.Text.TrimStart('#');
            if (hex.Length == 6)
            {
                SelectColorMode(hex);
                UpdateColorPreviewSwatch(hex);
            }
        };

        // Save button
        SaveButton.Click += (_, _) =>
        {
            _vm.SaveCommand.Execute(null);
            SaveButton.Visibility = Visibility.Collapsed;
        };
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_vm.HasUnsavedChanges))
                Dispatcher.Invoke(() => SaveButton.Visibility = _vm.HasUnsavedChanges
                    ? Visibility.Visible : Visibility.Collapsed);
        };

        RenderStylePreviews();
        UpdatePreview();

        // Re-apply highlight after visual tree is ready (FindResource may fail during construction)
        Loaded += (_, _) => HighlightSelectedSwatch(_selectedColorMode);
    }

    private void SelectColorMode(string mode)
    {
        _selectedColorMode = mode;
        if (mode == "auto")
        {
            _vm.MonochromeMode = false;
            _vm.UseCustomColor = false;
            _vm.CustomColorHex = null;
        }
        else if (mode == "mono")
        {
            _vm.MonochromeMode = true;
            _vm.UseCustomColor = false;
            _vm.CustomColorHex = null;
        }
        else
        {
            _vm.MonochromeMode = false;
            _vm.UseCustomColor = true;
            _vm.CustomColorHex = mode;
        }
        HighlightSelectedSwatch(mode);
        UpdatePreview();
    }

    private double GetCurrentPercentage()
    {
        var profileService = App.Services.GetRequiredService<IProfileService>();
        var usage = profileService.ActiveProfile?.ClaudeUsage;
        return usage?.SessionPercentage ?? 42;
    }

    private void BuildColorSwatches()
    {
        foreach (var (hex, label) in PresetColors)
        {
            Border swatch;
            if (hex == "auto")
            {
                // Multi-color gradient to represent status-based coloring
                swatch = new Border
                {
                    Width = 28, Height = 28,
                    CornerRadius = new CornerRadius(4),
                    Background = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new(Color.FromRgb(0x4C, 0xAF, 0x50), 0.0),
                            new(Color.FromRgb(0xFF, 0x98, 0x00), 0.5),
                            new(Color.FromRgb(0xF4, 0x43, 0x36), 1.0),
                        }, 0),
                };
            }
            else if (hex == "mono")
            {
                // Half black / half white to represent monochrome
                swatch = new Border
                {
                    Width = 28, Height = 28,
                    CornerRadius = new CornerRadius(4),
                    Background = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new(Colors.White, 0.0),
                            new(Colors.White, 0.5),
                            new(Color.FromRgb(0x33, 0x33, 0x33), 0.5),
                            new(Color.FromRgb(0x33, 0x33, 0x33), 1.0),
                        },
                        new Point(0, 0), new Point(1, 1)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0)),
                };
            }
            else
            {
                var color = (Color)ColorConverter.ConvertFromString("#" + hex);
                swatch = new Border
                {
                    Width = 28, Height = 28,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(color),
                };
            }

            swatch.Margin = new Thickness(0, 0, 6, 6);
            swatch.Cursor = System.Windows.Input.Cursors.Hand;
            swatch.BorderThickness = new Thickness(2);
            swatch.BorderBrush = Brushes.Transparent;
            swatch.Tag = hex;

            if (!string.IsNullOrEmpty(label))
            {
                swatch.ToolTip = label;
            }

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                SelectColorMode(hex);
                HexColorInput.Text = (hex != "auto" && hex != "mono") ? hex : "";
                UpdateColorPreviewSwatch(hex);
            };

            ColorSwatches.Children.Add(swatch);
        }

        HighlightSelectedSwatch(_selectedColorMode);
    }

    private void HighlightSelectedSwatch(string? selectedHex)
    {
        foreach (Border child in ColorSwatches.Children)
        {
            var isSelected = child.Tag?.ToString() == selectedHex;
            child.BorderBrush = isSelected
                ? (Brush)FindResource("MaterialDesign.Brush.Primary")
                : Brushes.Transparent;
        }
    }

    private void UpdateColorPreviewSwatch(string? hex)
    {
        if (hex == "auto" || hex == "mono" || string.IsNullOrEmpty(hex))
        {
            ColorPreviewSwatch.Background = Brushes.Transparent;
            return;
        }
        if (hex.Length == 6)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString("#" + hex);
                ColorPreviewSwatch.Background = new SolidColorBrush(color);
                return;
            }
            catch { }
        }
        ColorPreviewSwatch.Background = Brushes.Transparent;
    }

    private string? GetCustomColorForPreview()
    {
        return _vm.UseCustomColor ? _vm.CustomColorHex : null;
    }

    private void RenderStylePreviews()
    {
        var pct = GetCurrentPercentage();
        var displayPct = UsageStatusCalculator.GetDisplayPercentage(pct, _vm.ShowRemainingPercentage);
        var status = UsageStatusCalculator.CalculateStatus(pct, _vm.ShowRemainingPercentage);
        var mono = _vm.MonochromeMode;
        var dark = App.IsSystemDarkMode();
        var cc = GetCustomColorForPreview();

        PreviewBatteryIcon.Source = _renderer.RenderPreviewImage(displayPct, status, MenuBarIconStyle.Battery, mono, dark, 32, cc);
        PreviewBarIcon.Source = _renderer.RenderPreviewImage(displayPct, status, MenuBarIconStyle.ProgressBar, mono, dark, 32, cc);
        PreviewPercentIcon.Source = _renderer.RenderPreviewImage(displayPct, status, MenuBarIconStyle.Percentage, mono, dark, 32, cc);
        PreviewRingIcon.Source = _renderer.RenderPreviewImage(displayPct, status, MenuBarIconStyle.Ring, mono, dark, 32, cc);
        PreviewDotIcon.Source = _renderer.RenderPreviewImage(displayPct, status, MenuBarIconStyle.Compact, mono, dark, 32, cc);
    }

    private void UpdatePreview()
    {
        var pct = GetCurrentPercentage();
        var displayPct = UsageStatusCalculator.GetDisplayPercentage(pct, _vm.ShowRemainingPercentage);
        var status = UsageStatusCalculator.CalculateStatus(pct, _vm.ShowRemainingPercentage);
        var isDark = _vm.Theme == "dark" || (_vm.Theme == "auto" && App.IsSystemDarkMode());
        var cc = GetCustomColorForPreview();

        PreviewImage.Source = _renderer.RenderPreviewImage(
            displayPct, status, _vm.SessionIconStyle, _vm.MonochromeMode, isDark, 48, cc);

        PreviewBorder.Background = new SolidColorBrush(isDark
            ? ThemeColors.Get("TextPrimary")
            : ThemeColors.Get("SurfaceFooter"));

        var styleName = _vm.SessionIconStyle switch
        {
            MenuBarIconStyle.Battery => "Battery",
            MenuBarIconStyle.ProgressBar => "Bar",
            MenuBarIconStyle.Percentage => "Percentage",
            MenuBarIconStyle.Ring => "Ring",
            MenuBarIconStyle.Compact => "Dot",
            _ => ""
        };
        PreviewLabel.Text = $"{styleName} · {FormatterHelper.FormatPercentage(displayPct)} used";

        RenderStylePreviews();
    }

    private void SetIconStyleRadio(MenuBarIconStyle style)
    {
        StyleBattery.IsChecked = style == MenuBarIconStyle.Battery;
        StyleBar.IsChecked = style == MenuBarIconStyle.ProgressBar;
        StylePercent.IsChecked = style == MenuBarIconStyle.Percentage;
        StyleRing.IsChecked = style == MenuBarIconStyle.Ring;
        StyleCompact.IsChecked = style == MenuBarIconStyle.Compact;
    }
}
