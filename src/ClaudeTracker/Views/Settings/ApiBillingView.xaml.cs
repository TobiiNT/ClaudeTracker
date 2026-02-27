using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class ApiBillingView : UserControl
{
    private readonly ApiBillingViewModel _vm;

    public ApiBillingView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ApiBillingViewModel>();
        DataContext = _vm;

        TestButton.Click += async (_, _) =>
        {
            _vm.ApiKey = ApiKeyInput.Text;
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.TestConnectionCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };

        DisconnectButton.Click += (_, _) => _vm.DisconnectCommand.Execute(null);
        SaveButton.Click += async (_, _) =>
        {
            _vm.SelectedOrg = OrgCombo.SelectedItem as Models.APIOrganization;
            await _vm.SelectOrganizationCommand.ExecuteAsync(null);
        };

        OrgCombo.ItemsSource = _vm.Organizations;

        _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            ConnectedPanel.Visibility = _vm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            SetupPanel.Visibility = _vm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = _vm.TestStatus;
            OrgPickerPanel.Visibility = _vm.ShowOrgPicker ? Visibility.Visible : Visibility.Collapsed;
        });
    }
}
