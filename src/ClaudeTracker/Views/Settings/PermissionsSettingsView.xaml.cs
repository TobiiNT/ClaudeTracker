using System.Windows;
using System.Windows.Controls;
using ClaudeTracker.ViewModels;
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
