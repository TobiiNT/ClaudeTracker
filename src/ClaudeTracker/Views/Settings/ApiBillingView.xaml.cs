using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        ChangeUserButton.Click += async (_, _) =>
        {
            await _vm.ChangeUserCommand.ExecuteAsync(null);
        };

        // Org dropdown: only fetch on deliberate click selection, block scroll wheel
        OrgCombo.PreviewMouseWheel += ComboBox_BlockScrollWheel;
        OrgCombo.DropDownClosed += async (_, _) =>
        {
            var selected = OrgCombo.SelectedItem as Models.APIOrganization;
            if (selected == null || selected == _vm.SelectedOrg) return;
            _vm.SelectedOrg = selected;
            await _vm.SelectOrganizationCommand.ExecuteAsync(null);
        };

        UserCombo.PreviewMouseWheel += ComboBox_BlockScrollWheel;

        OrgCombo.ItemsSource = _vm.Organizations;
        UserCombo.ItemsSource = _vm.ClaudeCodeUsers;

        UserSaveButton.Click += (_, _) =>
        {
            _vm.SelectedUser = UserCombo.SelectedItem as Models.ClaudeCodeUserMetrics;
            _vm.SaveUserSelectionCommand.Execute(null);
        };

        _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            ConnectedPanel.Visibility = _vm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            SetupPanel.Visibility = _vm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = _vm.TestStatus;
            StatusText.Foreground = _vm.TestSuccess
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

            var wasOrgHidden = OrgPickerPanel.Visibility != Visibility.Visible;
            var wasUserHidden = UserPickerPanel.Visibility != Visibility.Visible;

            OrgPickerPanel.Visibility = _vm.ShowOrgPicker ? Visibility.Visible : Visibility.Collapsed;
            UserPickerPanel.Visibility = _vm.ShowUserPicker ? Visibility.Visible : Visibility.Collapsed;
            UserLoadingBar.Visibility = _vm.IsLoadingUsers ? Visibility.Visible : Visibility.Collapsed;
            TrackedUserText.Text = string.IsNullOrEmpty(_vm.TrackedUserLabel) ? "" : $"Tracking: {_vm.TrackedUserLabel}";
            ErrorText.Text = _vm.IsConfigured && !_vm.TestSuccess ? _vm.TestStatus : "";

            // Auto-scroll into view when panels first appear
            if (wasUserHidden && _vm.ShowUserPicker)
                UserPickerPanel.BringIntoView();
            else if (wasOrgHidden && _vm.ShowOrgPicker)
                OrgPickerPanel.BringIntoView();
        });
    }

    /// <summary>Prevents mouse wheel from changing ComboBox selection when dropdown is closed.</summary>
    private static void ComboBox_BlockScrollWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox combo && !combo.IsDropDownOpen)
            e.Handled = true;
    }
}
