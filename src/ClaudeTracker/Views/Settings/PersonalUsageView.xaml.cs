using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // --- Profile detection for UI mode ---
        var isDefault = IsDefaultProfile();

        // Section 1: Show unified or explicit setup
        if (!isDefault)
        {
            DefaultSetupCard.Visibility = Visibility.Collapsed;
            ExplicitSetupPanel.Visibility = Visibility.Visible;
        }

        // Section 2: Show unified or explicit setup
        if (isDefault)
        {
            ApiAutoDetectCard.Visibility = Visibility.Visible;
            ApiExplicitSetupPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ApiAutoDetectCard.Visibility = Visibility.Collapsed;
            ApiExplicitSetupPanel.Visibility = Visibility.Visible;
            if (BrowserSignInWindow.IsWebView2Available())
                ApiBrowserCard.Visibility = Visibility.Visible;
        }

        // --- Claude.ai Subscription (CLI auto-detect only) ---
        AutoDetectButton.Click += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.AutoDetectCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };

        DisconnectButton.Click += (_, _) => _vm.DisconnectCommand.Execute(null);

        _vm.PropertyChanged += (_, _) => UpdateUI();

        // --- Claude Code API Usage ---

        // Default profile: API section Connect opens WebView2 directly
        ApiAutoDetectButton.Click += async (_, _) =>
        {
            if (!BrowserSignInWindow.IsWebView2Available())
            {
                ApiAutoDetectStatus.Text = "WebView2 not available. Use manual paste below.";
                ApiAutoDetectCard.Visibility = Visibility.Collapsed;
                ApiExplicitSetupPanel.Visibility = Visibility.Visible;
                return;
            }

            var window = new BrowserSignInWindow(Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
            window.Owner = Window.GetWindow(this);
            window.Show();
            var result = await window.ResultTask;
            if (result.HasValue)
            {
                _apiVm.ApiKey = result.Value.sessionKey;
                var profile = App.Services.GetRequiredService<IProfileService>().ActiveProfile;
                if (profile != null && result.Value.expiry.HasValue)
                    profile.ApiSessionKeyExpiry = result.Value.expiry;

                ApiLoadingBar.Visibility = Visibility.Visible;
                await _apiVm.TestConnectionCommand.ExecuteAsync(null);
                ApiLoadingBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                // User closed WebView2 without completing sign-in — show manual fallback
                ApiAutoDetectStatus.Text = "Sign-in cancelled. You can try again or use manual paste.";
                ApiAutoDetectCard.Visibility = Visibility.Collapsed;
                ApiExplicitSetupPanel.Visibility = Visibility.Visible;
                if (BrowserSignInWindow.IsWebView2Available())
                    ApiBrowserCard.Visibility = Visibility.Visible;
            }
        };

        // Additional profile: explicit CLI button (for Section 1)
        ExplicitCliButton.Click += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.AutoDetectCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
            ExplicitCliStatus.Text = _vm.AutoDetectStatusText;
        };

        // Additional profile: explicit session key test (for Section 1)
        SessionKeyTestButton.Click += async (_, _) =>
        {
            _vm.SessionKey = SessionKeyInput.Text;
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.TestConnectionCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };

        // Additional profile: API browser sign-in (for Section 2)
        ApiBrowserButton.Click += async (_, _) =>
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

        ApiTestButton.Click += async (_, _) =>
        {
            _apiVm.ApiKey = ApiKeyInput.Text;
            ApiLoadingBar.Visibility = Visibility.Visible;
            await _apiVm.TestConnectionCommand.ExecuteAsync(null);
            ApiLoadingBar.Visibility = Visibility.Collapsed;
        };

        ApiDisconnectButton.Click += (_, _) => _apiVm.DisconnectCommand.Execute(null);
        ApiChangeUserButton.Click += async (_, _) =>
        {
            await _apiVm.ChangeUserCommand.ExecuteAsync(null);
        };

        // Org dropdown: only fetch on deliberate click selection, block scroll wheel
        ApiOrgCombo.PreviewMouseWheel += ComboBox_BlockScrollWheel;
        ApiOrgCombo.DropDownClosed += async (_, _) =>
        {
            var selected = ApiOrgCombo.SelectedItem as Models.APIOrganization;
            if (selected == null || selected == _apiVm.SelectedOrg) return;
            _apiVm.SelectedOrg = selected;
            await _apiVm.SelectOrganizationCommand.ExecuteAsync(null);
        };

        ApiUserCombo.PreviewMouseWheel += ComboBox_BlockScrollWheel;

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

    /// <summary>Prevents mouse wheel from changing ComboBox selection when dropdown is closed.</summary>
    private static void ComboBox_BlockScrollWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox combo && !combo.IsDropDownOpen)
            e.Handled = true;
    }

    private void UpdateUI()
    {
        Dispatcher.Invoke(() =>
        {
            ConnectedPanel.Visibility = _vm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            SetupPanel.Visibility = _vm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            ConnectedText.Text = _vm.ConnectedLabel;
            ConnectedDetailText.Text = _vm.ConnectedDetail;

            AutoDetectStatus.Text = _vm.AutoDetectStatusText;
            AutoDetectStatus.Foreground = _vm.AutoDetectSuccess
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        });
    }

    private bool IsDefaultProfile()
    {
        var profileService = App.Services.GetRequiredService<IProfileService>();
        var profiles = profileService.Profiles;
        if (profiles.Count <= 1) return true;
        var active = profileService.ActiveProfile;
        return active != null && profiles.Count > 0 && profiles[0].Id == active.Id;
    }

    private void UpdateApiUI()
    {
        Dispatcher.Invoke(() =>
        {
            ApiConnectedPanel.Visibility = _apiVm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            ApiSetupPanel.Visibility = _apiVm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            ApiStatusText.Text = _apiVm.TestStatus;
            ApiStatusText.Foreground = _apiVm.TestSuccess
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

            var wasOrgHidden = ApiOrgPickerPanel.Visibility != Visibility.Visible;
            var wasUserHidden = ApiUserPickerPanel.Visibility != Visibility.Visible;

            ApiOrgPickerPanel.Visibility = _apiVm.ShowOrgPicker ? Visibility.Visible : Visibility.Collapsed;
            ApiUserPickerPanel.Visibility = _apiVm.ShowUserPicker ? Visibility.Visible : Visibility.Collapsed;
            ApiUserLoadingBar.Visibility = _apiVm.IsLoadingUsers ? Visibility.Visible : Visibility.Collapsed;
            ApiTrackedUserText.Text = string.IsNullOrEmpty(_apiVm.TrackedUserLabel) ? "" : $"Tracking: {_apiVm.TrackedUserLabel}";
            ApiErrorText.Text = _apiVm.IsConfigured && !_apiVm.TestSuccess ? _apiVm.TestStatus : "";

            // Auto-scroll into view when panels first appear
            if (wasUserHidden && _apiVm.ShowUserPicker)
                ApiUserPickerPanel.BringIntoView();
            else if (wasOrgHidden && _apiVm.ShowOrgPicker)
                ApiOrgPickerPanel.BringIntoView();
        });
    }
}
