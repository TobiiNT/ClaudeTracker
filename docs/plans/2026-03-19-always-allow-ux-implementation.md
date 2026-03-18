# Always-Allow UX Improvement Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Permissions settings tab to view/delete Claude Code always-allow rules, and enhance the permission popup with a blanket "Always allow [Tool]" button.

**Architecture:** New `ClaudeCodeSettingsService` reads/writes `~/.claude/settings.json` permissions. New `PermissionsSettingsView` tab displays rules in a list with delete buttons. `BuildAlwaysAllowButtons()` gets one extra button for blanket tool permission using existing `toolAlwaysAllow` response type.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit

---

### Task 1: Create ClaudeCodeSettingsService

Service to read and write Claude Code's `~/.claude/settings.json` permission rules.

**Files:**
- Create: `src/ClaudeTracker/Services/Interfaces/IClaudeCodeSettingsService.cs`
- Create: `src/ClaudeTracker/Services/ClaudeCodeSettingsService.cs`
- Test: `tests/ClaudeTracker.Tests/ClaudeCodeSettingsServiceTests.cs`

**Step 1: Write the failing tests**

```csharp
// tests/ClaudeTracker.Tests/ClaudeCodeSettingsServiceTests.cs
using System.Text.Json;
using Xunit;
using ClaudeTracker.Services;

namespace ClaudeTracker.Tests;

public class ClaudeCodeSettingsServiceTests
{
    [Fact]
    public void ParsePermissions_ReturnsRulesFromJson()
    {
        var json = """
        {
            "permissions": {
                "Allow tool: Edit": { "type": "toolAlwaysAllow", "tool": "Edit" },
                "Allow Bash: git *": { "type": "prefix", "behavior": "allow", "tool": "Bash", "rules": [{"toolName":"Bash","ruleContent":"git"}] }
            },
            "hooks": {}
        }
        """;

        var rules = ClaudeCodeSettingsService.ParsePermissions(json);

        Assert.Equal(2, rules.Count);
        Assert.Equal("Allow tool: Edit", rules[0].Key);
        Assert.Equal("Edit", rules[0].Tool);
        Assert.Equal("Allow Bash: git *", rules[1].Key);
        Assert.Equal("Bash", rules[1].Tool);
    }

    [Fact]
    public void ParsePermissions_EmptyPermissions_ReturnsEmpty()
    {
        var json = """{ "hooks": {} }""";
        var rules = ClaudeCodeSettingsService.ParsePermissions(json);
        Assert.Empty(rules);
    }

    [Fact]
    public void RemovePermission_RemovesKeyFromJson()
    {
        var json = """
        {
            "permissions": {
                "Allow tool: Edit": { "type": "toolAlwaysAllow", "tool": "Edit" },
                "Allow Bash: git *": { "type": "prefix", "tool": "Bash" }
            },
            "hooks": {}
        }
        """;

        var result = ClaudeCodeSettingsService.RemovePermission(json, "Allow tool: Edit");

        using var doc = JsonDocument.Parse(result);
        var perms = doc.RootElement.GetProperty("permissions");
        Assert.False(perms.TryGetProperty("Allow tool: Edit", out _));
        Assert.True(perms.TryGetProperty("Allow Bash: git *", out _));
        // Preserves other top-level keys
        Assert.True(doc.RootElement.TryGetProperty("hooks", out _));
    }

    [Fact]
    public void ParsePermissions_ExtractsDisplayLabel()
    {
        var json = """
        {
            "permissions": {
                "rule1": { "type": "toolAlwaysAllow", "tool": "Read" }
            }
        }
        """;

        var rules = ClaudeCodeSettingsService.ParsePermissions(json);
        Assert.Single(rules);
        // DisplayLabel should be human-readable
        Assert.False(string.IsNullOrWhiteSpace(rules[0].DisplayLabel));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudeTracker.Tests/ --filter "ClaudeCodeSettingsServiceTests" -v m`
Expected: Build failure — `ClaudeCodeSettingsService` does not exist

**Step 3: Create the interface**

```csharp
// src/ClaudeTracker/Services/Interfaces/IClaudeCodeSettingsService.cs
using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

public interface IClaudeCodeSettingsService
{
    List<PermissionRuleEntry> GetPermissions();
    void RemovePermission(string key);
}
```

**Step 4: Create the model**

Add to `src/ClaudeTracker/Models/HookModels.cs`:

```csharp
/// <summary>
/// Represents a permission rule entry from Claude Code's ~/.claude/settings.json
/// </summary>
public class PermissionRuleEntry
{
    public string Key { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
}
```

**Step 5: Create the service**

```csharp
// src/ClaudeTracker/Services/ClaudeCodeSettingsService.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Services;

public class ClaudeCodeSettingsService : IClaudeCodeSettingsService
{
    private static readonly object _fileLock = new();

    public List<PermissionRuleEntry> GetPermissions()
    {
        var path = Constants.ClaudeSettingsPath;
        if (!File.Exists(path)) return new();

        string json;
        lock (_fileLock)
        {
            json = File.ReadAllText(path);
        }
        return ParsePermissions(json);
    }

    public void RemovePermission(string key)
    {
        var path = Constants.ClaudeSettingsPath;
        if (!File.Exists(path)) return;

        string json;
        lock (_fileLock)
        {
            json = File.ReadAllText(path);
            var updated = RemovePermission(json, key);
            File.WriteAllText(path, updated);
        }
    }

    public static List<PermissionRuleEntry> ParsePermissions(string json)
    {
        var rules = new List<PermissionRuleEntry>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("permissions", out var perms))
            return rules;

        foreach (var prop in perms.EnumerateObject())
        {
            var entry = new PermissionRuleEntry
            {
                Key = prop.Name,
                RawJson = prop.Value.GetRawText()
            };

            if (prop.Value.TryGetProperty("tool", out var tool))
                entry.Tool = tool.GetString() ?? "";
            if (prop.Value.TryGetProperty("type", out var type))
                entry.Type = type.GetString() ?? "";

            // Build display label
            entry.DisplayLabel = BuildDisplayLabel(prop.Value, entry);

            rules.Add(entry);
        }

        return rules;
    }

    public static string RemovePermission(string json, string key)
    {
        var node = JsonNode.Parse(json)!;
        var perms = node["permissions"]?.AsObject();
        perms?.Remove(key);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildDisplayLabel(JsonElement value, PermissionRuleEntry entry)
    {
        // Try rules first (most specific)
        if (value.TryGetProperty("rules", out var rules) && rules.GetArrayLength() > 0)
        {
            var rule = rules[0];
            var toolName = rule.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "";
            var ruleContent = rule.TryGetProperty("ruleContent", out var rc) ? rc.GetString() ?? "" : "";

            if (!string.IsNullOrEmpty(ruleContent))
            {
                if (toolName == "Bash")
                {
                    var cmd = ruleContent.TrimStart();
                    var firstWord = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() ?? cmd;
                    return $"Allow {firstWord}:*";
                }
                var content = ruleContent.Length > 40 ? ruleContent[..37] + "..." : ruleContent;
                return $"Allow {content}";
            }

            if (!string.IsNullOrEmpty(toolName))
            {
                // MCP tool formatting
                if (toolName.StartsWith("mcp__"))
                {
                    var parts = toolName[5..].Split("__", 2);
                    if (parts.Length == 2)
                        return $"Allow {parts[0]}: {parts[1]}";
                }
                return $"Allow {toolName}";
            }
        }

        if (!string.IsNullOrEmpty(entry.Tool))
            return $"Allow {entry.Tool}";

        return entry.Key;
    }
}
```

**Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ClaudeTracker.Tests/ --filter "ClaudeCodeSettingsServiceTests" -v m`
Expected: All 4 tests PASS

**Step 7: Commit**

```bash
git add src/ClaudeTracker/Services/Interfaces/IClaudeCodeSettingsService.cs src/ClaudeTracker/Services/ClaudeCodeSettingsService.cs src/ClaudeTracker/Models/HookModels.cs tests/ClaudeTracker.Tests/ClaudeCodeSettingsServiceTests.cs
git commit -m "feat: add ClaudeCodeSettingsService for reading/writing permission rules"
```

---

### Task 2: Create PermissionsSettingsViewModel

ViewModel for the Permissions settings tab.

**Files:**
- Create: `src/ClaudeTracker/ViewModels/Settings/PermissionsSettingsViewModel.cs`
- Modify: `src/ClaudeTracker/App.xaml.cs` (DI registration)

**Step 1: Create the ViewModel**

```csharp
// src/ClaudeTracker/ViewModels/Settings/PermissionsSettingsViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Models;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.ViewModels.Settings;

public partial class PermissionsSettingsViewModel : ObservableObject
{
    private readonly IClaudeCodeSettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<PermissionRuleEntry> _rules = new();

    [ObservableProperty]
    private bool _isEmpty = true;

    public PermissionsSettingsViewModel(IClaudeCodeSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadRules();
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadRules();
    }

    [RelayCommand]
    private void DeleteRule(string key)
    {
        _settingsService.RemovePermission(key);
        LoadRules();
    }

    private void LoadRules()
    {
        var loaded = _settingsService.GetPermissions();
        Rules = new ObservableCollection<PermissionRuleEntry>(loaded);
        IsEmpty = Rules.Count == 0;
    }
}
```

**Step 2: Register in DI**

In `src/ClaudeTracker/App.xaml.cs`, add to `ConfigureServices()`:

```csharp
services.AddSingleton<IClaudeCodeSettingsService, ClaudeCodeSettingsService>();
services.AddTransient<PermissionsSettingsViewModel>();
```

Add near existing settings ViewModel registrations (around line 400).

**Step 3: Verify build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/ClaudeTracker/ViewModels/Settings/PermissionsSettingsViewModel.cs src/ClaudeTracker/App.xaml.cs
git commit -m "feat: add PermissionsSettingsViewModel with load/delete/refresh"
```

---

### Task 3: Create PermissionsSettingsView (XAML + code-behind)

The Permissions tab UI.

**Files:**
- Create: `src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml`
- Create: `src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml.cs`

**Step 1: Create the XAML**

Follow the pattern from `HooksSettingsView.xaml`: ScrollViewer → StackPanel → MaxWidth 480.

```xml
<!-- src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml -->
<UserControl x:Class="ClaudeTracker.Views.Settings.PermissionsSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes">
    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="24,16" MaxWidth="480" HorizontalAlignment="Left">
            <!-- Title -->
            <TextBlock Text="Permissions" FontSize="20" FontWeight="Bold"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" />
            <TextBlock Text="Always-allow rules from Claude Code settings"
                       FontSize="12" Opacity="0.6" Margin="0,2,0,16"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" />

            <!-- Empty state -->
            <TextBlock x:Name="EmptyText" Text="No permission rules found."
                       FontSize="13" Opacity="0.5" Margin="0,8,0,16"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                       Visibility="Collapsed" />

            <!-- Rules list -->
            <ItemsControl x:Name="RulesList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border BorderThickness="0,0,0,0.5"
                                BorderBrush="{DynamicResource MaterialDesign.Brush.ForegroundLight}"
                                Padding="0,6">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>

                                <StackPanel Grid.Column="0" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding DisplayLabel}" FontSize="13"
                                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                                               TextTrimming="CharacterEllipsis" />
                                    <TextBlock Text="{Binding Key}" FontSize="10" Opacity="0.5"
                                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                                               TextTrimming="CharacterEllipsis" />
                                </StackPanel>

                                <Button Grid.Column="1" x:Name="DeleteBtn"
                                        Content="✕" FontSize="12"
                                        Width="28" Height="28"
                                        ToolTip="Remove this rule"
                                        Click="DeleteRule_Click"
                                        Tag="{Binding Key}"
                                        Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                                        Background="Transparent" BorderThickness="0"
                                        Cursor="Hand" VerticalAlignment="Center" />
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Refresh button -->
            <Button x:Name="RefreshButton" Content="Refresh" Margin="0,16,0,0"
                    HorizontalAlignment="Left" Height="32" Padding="16,0"
                    Style="{StaticResource MaterialDesignOutlinedButton}" />
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

**Step 2: Create the code-behind**

```csharp
// src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using ClaudeTracker.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeTracker.Views.Settings;

public partial class PermissionsSettingsView : UserControl
{
    private readonly PermissionsSettingsViewModel _vm;

    public PermissionsSettingsView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<PermissionsSettingsViewModel>();
        DataContext = _vm;

        RulesList.ItemsSource = _vm.Rules;
        UpdateEmptyState();

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_vm.Rules) || args.PropertyName == nameof(_vm.IsEmpty))
            {
                Dispatcher.Invoke(() =>
                {
                    RulesList.ItemsSource = _vm.Rules;
                    UpdateEmptyState();
                });
            }
        };

        RefreshButton.Click += (_, _) =>
        {
            _vm.RefreshCommand.Execute(null);
            RulesList.ItemsSource = _vm.Rules;
            UpdateEmptyState();
        };
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            _vm.DeleteRuleCommand.Execute(key);
            RulesList.ItemsSource = _vm.Rules;
            UpdateEmptyState();
        }
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

**Step 3: Verify build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml src/ClaudeTracker/Views/Settings/PermissionsSettingsView.xaml.cs
git commit -m "feat: add PermissionsSettingsView for viewing and deleting rules"
```

---

### Task 4: Register Permissions tab in SettingsWindow

Wire the new tab into the sidebar navigation.

**Files:**
- Modify: `src/ClaudeTracker/Views/SettingsWindow.xaml` (add RadioButton)
- Modify: `src/ClaudeTracker/Views/SettingsWindow.xaml.cs` (add navigation handler)

**Step 1: Add sidebar button**

In `SettingsWindow.xaml`, after the Hooks RadioButton (line 44), add:

```xml
                    <RadioButton x:Name="NavPermissions" Content="Permissions"
                                 Style="{StaticResource SidebarButton}"
                                 GroupName="SettingsNav" />
```

**Step 2: Add navigation handler**

In `SettingsWindow.xaml.cs`, add alongside existing handlers (around line 33):

```csharp
NavPermissions.Checked += (_, _) => ShowView<PermissionsSettingsView>();
```

Add using: `using ClaudeTracker.Views.Settings;` (likely already present).

**Step 3: Verify build**

Run: `dotnet build src/ClaudeTracker/ClaudeTracker.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/ClaudeTracker/Views/SettingsWindow.xaml src/ClaudeTracker/Views/SettingsWindow.xaml.cs
git commit -m "feat: add Permissions tab to settings sidebar"
```

---

### Task 5: Add blanket "Always allow [Tool]" button to permission popup

Enhance `BuildAlwaysAllowButtons()` to add an extra blanket tool permission button.

**Files:**
- Modify: `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs:584-644`
- Test: `tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs`

**Step 1: Write the failing test**

Add to `PermissionRequestHandlerTests.cs`:

```csharp
[Fact]
public void BuildResponseJson_AlwaysAllow_ToolAlwaysAllow_NoRawJson()
{
    // Blanket "Always allow [Tool]" button uses toolAlwaysAllow without RawJson
    var result = new PermissionDecisionResult
    {
        Decision = PermissionDecision.AlwaysAllow,
        AppliedSuggestion = new PermissionSuggestion
        {
            Type = "toolAlwaysAllow",
            Tool = "Bash"
            // No RawJson — triggers fallback path
        }
    };

    var json = PermissionRequestHandler.BuildResponseJson(result);

    Assert.NotNull(json);
    using var doc = JsonDocument.Parse(json!);
    var decision = doc.RootElement
        .GetProperty("hookSpecificOutput")
        .GetProperty("decision");
    Assert.Equal("allow", decision.GetProperty("behavior").GetString());
    var perms = decision.GetProperty("updatedPermissions");
    Assert.Equal(1, perms.GetArrayLength());
    Assert.Equal("toolAlwaysAllow", perms[0].GetProperty("type").GetString());
    Assert.Equal("Bash", perms[0].GetProperty("tool").GetString());
}
```

**Step 2: Run test to verify it passes (existing code already handles this)**

Run: `dotnet test tests/ClaudeTracker.Tests/ --filter "BuildResponseJson_AlwaysAllow_ToolAlwaysAllow_NoRawJson" -v m`
Expected: PASS — the existing `BuildResponseJson` fallback path (lines 182-188) already handles this case

**Step 3: Add blanket button to BuildAlwaysAllowButtons**

In `src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs`, at the end of `BuildAlwaysAllowButtons()` method (after the foreach loop, before closing brace at line 644), add:

```csharp
        // Add blanket "Always allow [Tool]" button if not already present
        var toolName = _info.ToolName;
        if (!string.IsNullOrEmpty(toolName))
        {
            var blanketLabel = $"Always allow {toolName}";
            var blanketKey = $"{blanketLabel}|{toolName}|";
            if (seenKeys.Add(blanketKey))
            {
                var blanketSuggestion = new PermissionSuggestion
                {
                    Type = "toolAlwaysAllow",
                    Tool = toolName
                };

                var btn = new Button
                {
                    Content = blanketLabel,
                    Height = 28,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = $"Always allow all {toolName} operations"
                };

                var style = new Style(typeof(Button));
                var template = new ControlTemplate(typeof(Button));
                var borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1)));
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));

                var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                borderFactory.AppendChild(contentFactory);

                template.VisualTree = borderFactory;
                borderFactory.Name = "Bd";
                style.Setters.Add(new Setter(Control.TemplateProperty, template));
                btn.Style = style;

                btn.Click += (_, _) =>
                {
                    SetDecision(new PermissionDecisionResult
                    {
                        Decision = PermissionDecision.AlwaysAllow,
                        AppliedSuggestion = blanketSuggestion
                    });
                };

                AlwaysAllowPanel.Children.Add(btn);
            }
        }
```

Note: Uses a slightly darker blue (`0x0D47A1`) to visually distinguish from Claude Code's suggestions (`0x1E88E5`).

**Step 4: Run all tests**

Run: `dotnet test tests/ClaudeTracker.Tests/ -v m`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ClaudeTracker/Views/PermissionRequestPopup.xaml.cs tests/ClaudeTracker.Tests/PermissionRequestHandlerTests.cs
git commit -m "feat: add blanket 'Always allow [Tool]' button to permission popup"
```

---

### Task 6: Full build + test verification

**Step 1: Run full build**

Run: `dotnet build --configuration Release`
Expected: Build succeeded with 0 errors

**Step 2: Run all tests**

Run: `dotnet test -v m`
Expected: All tests pass

**Step 3: Final commit (if any fixups needed)**

Only if build/test revealed issues.
