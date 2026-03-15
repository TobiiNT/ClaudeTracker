using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Views;

public partial class PermissionRequestPopup : Window
{
    private readonly PermissionRequestInfo _info;
    private readonly TaskCompletionSource<HookResponse> _responseSource;
    private bool _decided;

    // AskUserQuestion state
    private readonly Dictionary<int, HashSet<int>> _askSelections = new();
    private readonly Dictionary<int, bool> _askMultiSelect = new();
    private readonly Dictionary<int, TextBox?> _askOtherTextBoxes = new();
    private int _askQuestionCount;

    public PermissionRequestPopup(PermissionRequestInfo info, TaskCompletionSource<HookResponse> responseSource)
    {
        InitializeComponent();

        _info = info;
        _responseSource = responseSource;

        // Set header info
        ProjectText.Text = string.IsNullOrEmpty(info.Cwd)
            ? "Unknown project"
            : info.Cwd;

        ToolNameText.Text = info.ToolName;
        var desc = GetStringValue(info.ToolInput, "description");
        if (!string.IsNullOrEmpty(desc))
        {
            ToolDescText.Text = "\u2013 " + desc;
            ToolDescText.Visibility = System.Windows.Visibility.Visible;
        }
        ToolInputText.Text = FormatToolInput(info.ToolName, info.ToolInput);

        // Set contextual button labels
        AllowButton.Content = info.ToolName == "AskUserQuestion" ? "Submit" : $"Allow {info.ToolName}";
        AllowButton.ToolTip = $"Allow this {info.ToolName} call once";

        // Build "Always Allow" buttons from suggestions
        BuildAlwaysAllowButtons(info.PermissionSuggestions);

        // Setup preview panels for special tools
        SetupPreview(info.ToolName, info.ToolInput);

        // Auto-close if response resolved externally (pipe disconnect)
        _ = Task.Run(async () =>
        {
            try
            {
                await _responseSource.Task;
            }
            catch { /* ignored */ }

            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_decided)
                {
                    _decided = true;
                    Close();
                }
            });
        });

        // Register with popup stack manager
        PopupStackManager.Register(this);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position at bottom-right
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;

        // Reposition all stacked popups
        PopupStackManager.RepositionAll();

        // Slide-up + fade-in animation
        var slideAnim = new DoubleAnimation
        {
            From = Top + 40,
            To = Top,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        BeginAnimation(TopProperty, slideAnim);
        BeginAnimation(OpacityProperty, fadeAnim);

        // Play attention sound
        try { SystemSounds.Exclamation.Play(); }
        catch { /* sound not critical */ }
    }

    private void SetupPreview(string toolName, Dictionary<string, object> toolInput)
    {
        switch (toolName)
        {
            case "Edit" when toolInput.ContainsKey("old_string") && toolInput.ContainsKey("new_string"):
                ShowDiffPreview(
                    toolInput["old_string"]?.ToString() ?? "",
                    toolInput["new_string"]?.ToString() ?? "");
                break;

            case "Write" when toolInput.ContainsKey("content"):
                ShowWritePreview(toolInput["content"]?.ToString() ?? "");
                break;

            case "AskUserQuestion":
                ShowAskPreview(toolInput);
                break;
        }
    }

    private void ShowDiffPreview(string oldText, string newText)
    {
        DiffPanel.Visibility = Visibility.Visible;

        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');

        foreach (var line in oldLines)
        {
            OldContent.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var line in newLines)
        {
            NewContent.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Sync scroll positions
        OldScroll.ScrollChanged += (_, args) =>
        {
            NewScroll.ScrollToVerticalOffset(args.VerticalOffset);
            NewScroll.ScrollToHorizontalOffset(args.HorizontalOffset);
        };
        NewScroll.ScrollChanged += (_, args) =>
        {
            OldScroll.ScrollToVerticalOffset(args.VerticalOffset);
            OldScroll.ScrollToHorizontalOffset(args.HorizontalOffset);
        };
    }

    private void ShowWritePreview(string content)
    {
        WritePanel.Visibility = Visibility.Visible;

        var lines = content.Split('\n');
        var maxLines = 30;
        var displayLines = lines.Take(maxLines).ToArray();

        for (int i = 0; i < displayLines.Length; i++)
        {
            var linePanel = new StackPanel { Orientation = Orientation.Horizontal };
            linePanel.Children.Add(new TextBlock
            {
                Text = $"{i + 1,4} ",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                MinWidth = 35
            });
            linePanel.Children.Add(new TextBlock
            {
                Text = displayLines[i],
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                TextWrapping = TextWrapping.Wrap
            });
            WriteContent.Children.Add(linePanel);
        }

        if (lines.Length > maxLines)
        {
            WriteContent.Children.Add(new TextBlock
            {
                Text = $"... {lines.Length - maxLines} more lines",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 4, 0, 0),
                FontStyle = FontStyles.Italic
            });
        }
    }

    private void ShowAskPreview(Dictionary<string, object> toolInput)
    {
        AskPanel.Visibility = Visibility.Visible;

        // Disable Allow until all questions are answered
        AllowButton.IsEnabled = false;

        // Parse questions - may be a JSON string or list
        var questions = ParseAskQuestions(toolInput);
        _askQuestionCount = questions.Count;

        for (int qIdx = 0; qIdx < questions.Count; qIdx++)
        {
            var q = questions[qIdx];
            _askSelections[qIdx] = new HashSet<int>();
            _askMultiSelect[qIdx] = q.MultiSelect;
            _askOtherTextBoxes[qIdx] = null;

            // Question text
            AskContent.Children.Add(new TextBlock
            {
                Text = q.Question,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, qIdx > 0 ? 12 : 0, 0, 6)
            });

            if (q.MultiSelect)
            {
                AskContent.Children.Add(new TextBlock
                {
                    Text = "(select multiple)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            // Options
            for (int oIdx = 0; oIdx < q.Options.Count; oIdx++)
            {
                var opt = q.Options[oIdx];
                var questionIndex = qIdx;
                var optionIndex = oIdx;

                var optionBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand
                };

                var optionStack = new StackPanel { IsHitTestVisible = false };
                optionStack.Children.Add(new TextBlock
                {
                    Text = opt.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontWeight = FontWeights.Medium
                });

                if (!string.IsNullOrEmpty(opt.Description))
                {
                    optionStack.Children.Add(new TextBlock
                    {
                        Text = opt.Description,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                optionBorder.Child = optionStack;

                optionBorder.PreviewMouseLeftButtonDown += (_, _) =>
                {
                    ToggleAskOption(questionIndex, optionIndex, optionBorder);
                };

                AskContent.Children.Add(optionBorder);
            }

            // "Other" option with text box
            {
                var otherIndex = q.Options.Count;
                var questionIndex = qIdx;

                var otherBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand
                };

                var otherStack = new StackPanel();
                otherStack.Children.Add(new TextBlock
                {
                    Text = "Other...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontWeight = FontWeights.Medium,
                    IsHitTestVisible = false
                });

                var otherTextBox = new TextBox
                {
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 4, 0, 0),
                    Visibility = Visibility.Collapsed,
                    CaretBrush = new SolidColorBrush(Colors.White)
                };
                otherStack.Children.Add(otherTextBox);

                _askOtherTextBoxes[qIdx] = otherTextBox;
                otherBorder.Child = otherStack;

                // Hide textbox when it loses focus and is empty
                otherTextBox.LostFocus += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(otherTextBox.Text))
                    {
                        otherTextBox.Visibility = Visibility.Collapsed;
                        _askSelections[questionIndex].Remove(otherIndex);
                        otherBorder.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                        UpdateAllowButtonState();
                    }
                };

                otherBorder.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    // Don't toggle when clicking inside the visible TextBox
                    if (otherTextBox.IsVisible && otherTextBox.IsMouseOver)
                        return;

                    ToggleAskOption(questionIndex, otherIndex, otherBorder);
                    otherTextBox.Visibility = _askSelections[questionIndex].Contains(otherIndex)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    if (otherTextBox.Visibility == Visibility.Visible)
                        Dispatcher.BeginInvoke(() => otherTextBox.Focus());
                };

                AskContent.Children.Add(otherBorder);
            }
        }
    }

    private void ToggleAskOption(int questionIndex, int optionIndex, Border optionBorder)
    {
        var selections = _askSelections[questionIndex];
        var isMultiSelect = _askMultiSelect[questionIndex];

        if (selections.Contains(optionIndex))
        {
            selections.Remove(optionIndex);
            optionBorder.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }
        else
        {
            if (!isMultiSelect)
            {
                // Deselect all others visually — find sibling borders
                selections.Clear();
                ResetAskOptionColors(questionIndex);
            }
            selections.Add(optionIndex);
            optionBorder.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x5A));
        }

        UpdateAllowButtonState();
    }

    private void ResetAskOptionColors(int questionIndex)
    {
        // Walk AskContent children to find option borders for this question
        // Options are positioned after the question TextBlock(s)
        int currentQ = -1;
        foreach (var child in AskContent.Children)
        {
            if (child is TextBlock tb && tb.FontWeight == FontWeights.SemiBold && tb.FontSize == 12)
            {
                currentQ++;
            }
            else if (child is Border b && currentQ == questionIndex)
            {
                b.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            }
        }
    }

    private void UpdateAllowButtonState()
    {
        // Allow enabled only when all questions have at least one selection
        for (int i = 0; i < _askQuestionCount; i++)
        {
            if (!_askSelections.ContainsKey(i) || _askSelections[i].Count == 0)
            {
                AllowButton.IsEnabled = false;
                return;
            }
        }
        AllowButton.IsEnabled = true;
    }

    private List<AskQuestion> ParseAskQuestions(Dictionary<string, object> toolInput)
    {
        var result = new List<AskQuestion>();

        if (!toolInput.TryGetValue("questions", out var questionsObj))
            return result;

        string? questionsJson = null;

        if (questionsObj is string str)
        {
            // May be double-encoded JSON string
            questionsJson = str;
        }
        else if (questionsObj is JsonElement element)
        {
            questionsJson = element.GetRawText();
        }
        else if (questionsObj is List<object> list)
        {
            questionsJson = JsonSerializer.Serialize(list);
        }
        else
        {
            questionsJson = JsonSerializer.Serialize(questionsObj);
        }

        if (string.IsNullOrEmpty(questionsJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(questionsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var qElem in doc.RootElement.EnumerateArray())
                {
                    var question = new AskQuestion
                    {
                        Question = qElem.TryGetProperty("question", out var qText)
                            ? qText.GetString() ?? "" : "",
                        MultiSelect = qElem.TryGetProperty("multiSelect", out var ms)
                            && ms.ValueKind == JsonValueKind.True
                    };

                    if (qElem.TryGetProperty("options", out var optionsElem)
                        && optionsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var optElem in optionsElem.EnumerateArray())
                        {
                            question.Options.Add(new AskOption
                            {
                                Label = optElem.TryGetProperty("label", out var lbl)
                                    ? lbl.GetString() ?? "" : "",
                                Description = optElem.TryGetProperty("description", out var desc)
                                    ? desc.GetString() ?? "" : ""
                            });
                        }
                    }

                    result.Add(question);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to parse AskUserQuestion questions", ex);
        }

        return result;
    }

    private Dictionary<string, object> BuildAskUserAnswers()
    {
        var answers = new Dictionary<string, object>();
        var questions = ParseAskQuestions(_info.ToolInput);

        LoggingService.Instance.Log($"[PermPopup] BuildAskUserAnswers: {questions.Count} questions, selections={string.Join(",", _askSelections.Select(kv => $"q{kv.Key}:[{string.Join(",", kv.Value)}]"))}");

        for (int qIdx = 0; qIdx < questions.Count; qIdx++)
        {
            var q = questions[qIdx];
            var selections = _askSelections.GetValueOrDefault(qIdx, new HashSet<int>());
            var selectedLabels = new List<string>();

            foreach (var idx in selections)
            {
                if (idx < q.Options.Count)
                {
                    selectedLabels.Add(q.Options[idx].Label);
                    LoggingService.Instance.Log($"[PermPopup] Q{qIdx}: selected option {idx} = '{q.Options[idx].Label}'");
                }
                else
                {
                    // "Other" option
                    var textBox = _askOtherTextBoxes.GetValueOrDefault(qIdx);
                    var otherText = textBox?.Text?.Trim();
                    LoggingService.Instance.Log($"[PermPopup] Q{qIdx}: Other option, textBox={textBox != null}, text='{otherText}'");
                    if (!string.IsNullOrEmpty(otherText))
                        selectedLabels.Add(otherText);
                }
            }

            // Key by question text (Claude Code expects this format)
            var key = q.Question;
            if (q.MultiSelect)
                answers[key] = string.Join(", ", selectedLabels);
            else
                answers[key] = selectedLabels.FirstOrDefault() ?? "";

            LoggingService.Instance.Log($"[PermPopup] Q{qIdx}: answer['{key}'] = '{answers[key]}'");
        }

        // Return as the full updated tool input with answers merged
        var updatedInput = new Dictionary<string, object>(_info.ToolInput);
        updatedInput["answers"] = answers;
        return updatedInput;
    }

    private void BuildAlwaysAllowButtons(List<PermissionSuggestion> suggestions)
    {
        foreach (var suggestion in suggestions)
        {
            LoggingService.Instance.Log($"[PermPopup] Suggestion: type={suggestion.Type}, behavior={suggestion.Behavior}, tool={suggestion.Tool}, prefix={suggestion.Prefix}, rules={suggestion.Rules.Count}{(suggestion.Rules.Count > 0 ? $" [{suggestion.Rules[0].ToolName}:{suggestion.Rules[0].RuleContent}]" : "")}, dirs={suggestion.Directories.Count}");
            var label = suggestion.DisplayLabel;
            if (string.IsNullOrWhiteSpace(label)) continue;

            var btn = new Button
            {
                Content = label,
                Height = 28,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.White),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = suggestion.Rules.Count > 0
                    ? $"Always allow {suggestion.Rules[0].ToolName}({suggestion.Rules[0].RuleContent})"
                    : $"Always allow: {label}"
            };

            var style = new Style(typeof(Button));
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), "Bd"));

            // Use a named border for triggers
            borderFactory.Name = "Bd";

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            btn.Style = style;

            var capturedSuggestion = suggestion;
            btn.Click += (_, _) =>
            {
                SetDecision(new PermissionDecisionResult
                {
                    Decision = PermissionDecision.AlwaysAllow,
                    AppliedSuggestion = capturedSuggestion
                });
            };

            AlwaysAllowPanel.Children.Add(btn);
        }
    }

    private void SetDecision(PermissionDecisionResult result)
    {
        if (_decided) return;
        _decided = true;

        var jsonOutput = Services.Handlers.PermissionRequestHandler.BuildResponseJson(result);
        LoggingService.Instance.Log($"[PermPopup] Decision={result.Decision}, HasUpdatedInput={result.UpdatedInput != null}, JsonOutput={jsonOutput ?? "(null)"}");
        _responseSource.TrySetResult(new HookResponse
        {
            RequestId = _info.ToolName,
            Success = true,
            JsonOutput = string.IsNullOrEmpty(jsonOutput) ? null : jsonOutput
        });
        Close();
    }

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        if (_info.ToolName == "AskUserQuestion")
        {
            var result = new PermissionDecisionResult
            {
                Decision = PermissionDecision.Allow,
                UpdatedInput = BuildAskUserAnswers()
            };
            SetDecision(result);
        }
        else
        {
            SetDecision(new PermissionDecisionResult { Decision = PermissionDecision.Allow });
        }
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        SetDecision(new PermissionDecisionResult { Decision = PermissionDecision.Deny });
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        SetDecision(new PermissionDecisionResult { Decision = PermissionDecision.HandleInTerminal });
    }

    protected override void OnClosed(EventArgs e)
    {
        // If closed without deciding (e.g., Alt+F4), fall back to terminal
        if (!_decided)
        {
            _decided = true;
            var jsonOutput = Services.Handlers.PermissionRequestHandler.BuildResponseJson(
                new PermissionDecisionResult { Decision = PermissionDecision.HandleInTerminal });
            _responseSource.TrySetResult(new HookResponse
            {
                RequestId = _info.ToolName,
                Success = true,
                JsonOutput = string.IsNullOrEmpty(jsonOutput) ? null : jsonOutput
            });
        }

        base.OnClosed(e);
    }

    private static string FormatToolInput(string toolName, Dictionary<string, object> toolInput)
    {
        return toolName switch
        {
            "Bash" or "bash" => GetStringValue(toolInput, "command"),
            "Read" or "read" => GetStringValue(toolInput, "file_path"),
            "Edit" or "edit" => GetStringValue(toolInput, "file_path"),
            "Write" or "write" => GetStringValue(toolInput, "file_path"),
            "Glob" or "glob" => GetStringValue(toolInput, "pattern"),
            "Grep" or "grep" => $"{GetStringValue(toolInput, "pattern")} in {GetStringValue(toolInput, "path")}",
            "WebFetch" or "WebSearch" => GetStringValue(toolInput, "url", GetStringValue(toolInput, "query")),
            "AskUserQuestion" => GetStringValue(toolInput, "question", "(interactive question)"),
            _ => FormatDictionaryCompact(toolInput)
        };
    }

    private static string GetStringValue(Dictionary<string, object> dict, string key, string fallback = "")
    {
        if (dict.TryGetValue(key, out var value))
        {
            var str = value?.ToString() ?? "";
            return string.IsNullOrEmpty(str) ? fallback : str;
        }
        return fallback;
    }

    private static string FormatDictionaryCompact(Dictionary<string, object> dict)
    {
        if (dict.Count == 0) return "(no input)";

        var parts = dict.Select(kvp =>
        {
            var val = kvp.Value?.ToString() ?? "";
            if (val.Length > 80) val = val[..77] + "...";
            return $"{kvp.Key}: {val}";
        });
        return string.Join("\n", parts);
    }

    // Helper types for AskUserQuestion parsing
    private class AskQuestion
    {
        public string Question { get; set; } = "";
        public List<AskOption> Options { get; set; } = new();
        public bool MultiSelect { get; set; }
    }

    private class AskOption
    {
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
