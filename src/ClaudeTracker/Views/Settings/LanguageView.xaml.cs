using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class LanguageView : UserControl
{
    private readonly LanguageViewModel _vm;

    public LanguageView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<LanguageViewModel>();
        DataContext = _vm;

        var items = _vm.SupportedLanguages.Select(l => new { l.Code, l.Name }).ToList();
        LanguageList.ItemsSource = items;
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string code)
            _vm.SelectedLanguage = code;
    }
}
