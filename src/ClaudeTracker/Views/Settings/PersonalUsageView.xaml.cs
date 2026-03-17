using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class PersonalUsageView : UserControl
{
    private readonly PersonalUsageViewModel _vm;
    private readonly ApiBillingViewModel _apiVm;

    public PersonalUsageView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<PersonalUsageViewModel>();
        _apiVm = App.Services.GetRequiredService<ApiBillingViewModel>();
        DataContext = _vm;

        // --- Browser sign-in (WebView2) ---
        if (BrowserSignInWindow.IsWebView2Available())
        {
            BrowserSignInCard.Visibility = Visibility.Visible;
            BrowserApiSignInCard.Visibility = Visibility.Visible;
        }

        BrowserSignInButton.Click += async (_, _) =>
        {
            var window = new BrowserSignInWindow("https://claude.ai/login", "claude.ai");
            window.Owner = Window.GetWindow(this);
            window.Show();
            var result = await window.ResultTask;
            if (result.HasValue)
            {
                SessionKeyInput.Text = result.Value.sessionKey;
                _vm.SessionKey = result.Value.sessionKey;

                var profile = App.Services.GetRequiredService<IProfileService>().ActiveProfile;
                if (profile != null && result.Value.expiry.HasValue)
                    profile.ClaudeSessionKeyExpiry = result.Value.expiry;

                LoadingBar.Visibility = Visibility.Visible;
                await _vm.TestConnectionCommand.ExecuteAsync(null);
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        };

        BrowserApiSignInButton.Click += async (_, _) =>
        {
            var window = new BrowserSignInWindow(Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
            window.Owner = Window.GetWindow(this);
            window.Show();
            var result = await window.ResultTask;
            if (result.HasValue)
            {
                ApiKeyInput.Text = result.Value.sessionKey;
                _apiVm.ApiKey = result.Value.sessionKey;

                var profile = App.Services.GetRequiredService<IProfileService>().ActiveProfile;
                if (profile != null && result.Value.expiry.HasValue)
                    profile.ApiSessionKeyExpiry = result.Value.expiry;

                ApiLoadingBar.Visibility = Visibility.Visible;
                await _apiVm.TestConnectionCommand.ExecuteAsync(null);
                ApiLoadingBar.Visibility = Visibility.Collapsed;
            }
        };

        // --- Claude.ai connection ---
        AutoDetectButton.Click += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.AutoDetectCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };

        TestButton.Click += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.TestConnectionCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };

        DisconnectButton.Click += (_, _) => _vm.DisconnectCommand.Execute(null);
        SaveOrgButton.Click += async (_, _) =>
        {
            _vm.SelectedOrg = OrgCombo.SelectedItem as Models.AccountInfo;
            await _vm.SelectOrganizationCommand.ExecuteAsync(null);
        };

        _vm.PropertyChanged += (_, _) => UpdateUI();

        SessionKeyInput.TextChanged += (_, _) => _vm.SessionKey = SessionKeyInput.Text;
        SessionKeyInput.Text = _vm.SessionKey;

        OrgCombo.ItemsSource = _vm.Organizations;

        // --- API Billing ---
        ApiTestButton.Click += async (_, _) =>
        {
            _apiVm.ApiKey = ApiKeyInput.Text;
            ApiLoadingBar.Visibility = Visibility.Visible;
            await _apiVm.TestConnectionCommand.ExecuteAsync(null);
            ApiLoadingBar.Visibility = Visibility.Collapsed;
        };

        ApiDisconnectButton.Click += (_, _) => _apiVm.DisconnectCommand.Execute(null);
        ApiSaveButton.Click += async (_, _) =>
        {
            _apiVm.SelectedOrg = ApiOrgCombo.SelectedItem as Models.APIOrganization;
            await _apiVm.SelectOrganizationCommand.ExecuteAsync(null);
        };

        ApiOrgCombo.ItemsSource = _apiVm.Organizations;
        ApiUserCombo.ItemsSource = _apiVm.ClaudeCodeUsers;

        ApiUserSaveButton.Click += (_, _) =>
        {
            _apiVm.SelectedUser = ApiUserCombo.SelectedItem as Models.ClaudeCodeUserMetrics;
            _apiVm.SaveUserSelectionCommand.Execute(null);
        };

        _apiVm.PropertyChanged += (_, _) => UpdateApiUI();

        UpdateUI();
        UpdateApiUI();
    }

    private void UpdateUI()
    {
        Dispatcher.Invoke(() =>
        {
            ConnectedPanel.Visibility = _vm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            SetupPanel.Visibility = _vm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            ConnectedText.Text = _vm.ConnectedLabel;
            ConnectedDetailText.Text = _vm.ConnectedDetail;
            TestStatusText.Text = _vm.TestStatus;
            TestStatusText.Foreground = _vm.TestSuccess
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            OrgPickerPanel.Visibility = _vm.ShowOrgPicker ? Visibility.Visible : Visibility.Collapsed;

            AutoDetectStatus.Text = _vm.AutoDetectStatusText;
            AutoDetectStatus.Foreground = _vm.AutoDetectSuccess
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        });
    }

    private void UpdateApiUI()
    {
        Dispatcher.Invoke(() =>
        {
            ApiConnectedPanel.Visibility = _apiVm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            ApiSetupPanel.Visibility = _apiVm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            ApiStatusText.Text = _apiVm.TestStatus;
            ApiOrgPickerPanel.Visibility = _apiVm.ShowOrgPicker ? Visibility.Visible : Visibility.Collapsed;
            ApiUserPickerPanel.Visibility = _apiVm.ShowUserPicker ? Visibility.Visible : Visibility.Collapsed;
        });
    }
}
