using System.Windows;
using System.Windows.Input;

namespace ClaudeTracker.Views;

public partial class InputDialog : Window
{
    public string ResultText { get; private set; } = "";

    public InputDialog(string title, string prompt, string hint = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        if (!string.IsNullOrEmpty(hint))
            InputBox.Text = hint;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ResultText = InputBox.Text.Trim();
            DialogResult = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
