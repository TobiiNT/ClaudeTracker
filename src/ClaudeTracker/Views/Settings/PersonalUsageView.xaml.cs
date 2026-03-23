using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.ViewModels;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Views.Settings;

public partial class PersonalUsageView : UserControl
{
    private readonly PersonalUsageViewModel _vm;
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;

    // API Console state
    private string _apiSessionKey = "";
    private APIOrganization? _selectedOrg;

    public PersonalUsageView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<PersonalUsageViewModel>();
        _apiService = App.Services.GetRequiredService<IClaudeApiService>();
        _profileService = App.Services.GetRequiredService<IProfileService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _refreshCoordinator = App.Services.GetRequiredService<IUsageRefreshCoordinator>();
        DataContext = _vm;

        // --- Subscription section ---
        AutoDetectButton.Click += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            await _vm.AutoDetectCommand.ExecuteAsync(null);
            LoadingBar.Visibility = Visibility.Collapsed;
        };
        DisconnectButton.Click += (_, _) => _vm.DisconnectCommand.Execute(null);
        _vm.PropertyChanged += (_, _) => UpdateSubscriptionUI();

        // --- API Console section ---
        ApiConnectButton.Click += async (_, _) => await OnApiConnect();
        ApiAutoSignInButton.Click += async (_, _) => await OnApiAutoSignIn();
        ApiOpenConsoleButton.Click += (_, _) => OnOpenConsole();

        ApiOrgCombo.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
        ApiOrgCombo.SelectionChanged += async (_, _) => await OnOrgSelected();

        ApiUserCombo.PreviewMouseWheel += ScrollHelper.RouteMouseWheelToParent;
        ApiConfirmButton.Click += (_, _) => OnApiConfirm();

        ApiDisconnectButton.Click += (_, _) => OnApiDisconnect();
        ApiChangeUserButton.Click += async (_, _) => await OnApiChangeUser();

        UpdateSubscriptionUI();
        UpdateApiConnectedUI();
    }

    // ── Subscription UI ──

    private void UpdateSubscriptionUI()
    {
        Dispatcher.Invoke(() =>
        {
            ConnectedPanel.Visibility = _vm.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            SetupPanel.Visibility = _vm.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
            ConnectedText.Text = _vm.ConnectedLabel;
            ConnectedDetailText.Text = _vm.ConnectedDetail;

            AutoDetectStatus.Text = _vm.AutoDetectStatusText;
            AutoDetectStatus.Foreground = _vm.AutoDetectSuccess
                ? new SolidColorBrush(ThemeColors.Get("StatusSafe"))
                : new SolidColorBrush(ThemeColors.Get("TextMuted"));
        });
    }

    // ── API Console: Connected state ──

    private void UpdateApiConnectedUI()
    {
        var profile = _profileService.ActiveProfile;
        var isConfigured = profile?.HasAPIConsole == true;

        ApiConnectedPanel.Visibility = isConfigured ? Visibility.Visible : Visibility.Collapsed;
        ApiSetupPanel.Visibility = isConfigured ? Visibility.Collapsed : Visibility.Visible;

        if (isConfigured && profile != null)
        {
            var orgName = profile.ApiOrganizationName;
            var userLabel = profile.ApiUserSearch;
            ApiConnectedText.Text = string.IsNullOrEmpty(orgName) ? "Connected" : $"Connected to {orgName}";
            ApiTrackedUserText.Text = string.IsNullOrEmpty(userLabel) ? "" : $"Tracking: {userLabel}";
        }
    }

    private void OnApiDisconnect()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var credentials = _profileService.LoadCredentials(profile.Id);
        credentials.ApiSessionKey = null;
        credentials.ApiOrganizationId = null;
        _profileService.SaveCredentials(profile.Id, credentials);

        profile.ApiUserSearch = null;
        profile.ApiOrganizationName = null;
        profile.ApiUsage = null;
        profile.PersonalMetrics = null;
        profile.DailyMetrics = null;
        _settingsService.Save();

        _refreshCoordinator.InvalidateApiCache();
        _refreshCoordinator.RefreshNow();

        // Reset setup UI
        ResetApiSetup();
        UpdateApiConnectedUI();
    }

    private async Task OnApiChangeUser()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var credentials = _profileService.LoadCredentials(profile.Id);
        if (string.IsNullOrEmpty(credentials.ApiSessionKey) || string.IsNullOrEmpty(credentials.ApiOrganizationId))
            return;

        _apiSessionKey = credentials.ApiSessionKey;
        _selectedOrg = new APIOrganization { Id = credentials.ApiOrganizationId, Name = profile.ApiOrganizationName ?? "" };

        // Show setup panel with step 3 directly
        ApiConnectedPanel.Visibility = Visibility.Collapsed;
        ApiSetupPanel.Visibility = Visibility.Visible;
        ApiStep1.Visibility = Visibility.Collapsed;
        ApiStep2.Visibility = Visibility.Collapsed;
        ApiSubtitle.Text = $"Connected to {_selectedOrg.DisplayName}";
        ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("StatusSafe"));

        await FetchUsersForOrg(_selectedOrg);
    }

    // ── API Console Step 1: Session Key ──

    private async Task OnApiAutoSignIn()
    {
        var window = new BrowserSignInWindow(Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
        window.Owner = Window.GetWindow(this);
        window.Show();
        var result = await window.ResultTask;

        if (!result.HasValue) return;

        ApiKeyInput.Text = result.Value.sessionKey;
        await OnApiConnect();
    }

    private void OnOpenConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msedge.exe",
                Arguments = "--auto-open-devtools-for-tabs https://platform.claude.com",
                UseShellExecute = true
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://platform.claude.com",
                UseShellExecute = true
            });
        }
    }

    private async Task OnApiConnect()
    {
        var key = ApiKeyInput.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiSubtitle.Text = "Please enter a session key";
            return;
        }

        ApiSubtitle.Text = "Validating session key...";
        ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("TextMuted"));
        ApiLoadingBar.Visibility = Visibility.Visible;
        ApiConnectButton.IsEnabled = false;

        try
        {
            var orgs = await _apiService.FetchConsoleOrganizations(key);
            if (orgs.Count == 0)
            {
                ApiSubtitle.Text = "No organizations found.";
                return;
            }

            _apiSessionKey = key;

            ApiSubtitle.Text = "Fetching billing data...";
            ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("StatusSafe"));
            ApiStep1.Visibility = Visibility.Collapsed;

            // Enrich orgs with spend data (best-effort)
            await EnrichOrgsWithSpend(orgs, key);

            if (orgs.Count == 1)
            {
                _selectedOrg = orgs[0];
                SaveApiConsoleOrg(orgs[0]);
                ApiSubtitle.Text = $"Connected to {orgs[0].DisplayName}";
                await FetchUsersForOrg(orgs[0]);
            }
            else
            {
                ApiSubtitle.Text = $"Found {orgs.Count} organizations.";
                ApiOrgCombo.ItemsSource = orgs;
                ApiStep2.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ApiSubtitle.Text = $"Failed: {ex.Message}";
            ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("StatusCritical"));
        }
        finally
        {
            ApiLoadingBar.Visibility = Visibility.Collapsed;
            ApiConnectButton.IsEnabled = true;
        }
    }

    // ── API Console Step 2: Organization ──

    private async Task OnOrgSelected()
    {
        if (ApiOrgCombo.SelectedItem is not APIOrganization org) return;
        _selectedOrg = org;

        // Save org immediately so API usage/limit refreshes right away
        SaveApiConsoleOrg(org);

        await FetchUsersForOrg(org);
    }

    private async Task FetchUsersForOrg(APIOrganization org)
    {
        ApiLoadingBar.Visibility = Visibility.Visible;
        ApiStep3.Visibility = Visibility.Collapsed;
        ApiTip.Visibility = Visibility.Collapsed;

        try
        {
            var users = await _apiService.FetchClaudeCodeAllUsers(org.Id, _apiSessionKey);
            if (users.Count > 0)
            {
                ApiUserCombo.ItemsSource = users;
                ApiStep2.Visibility = Visibility.Collapsed;
                ApiStep3.Visibility = Visibility.Visible;
                ApiStep3.BringIntoView();
            }
            else
            {
                ApiSubtitle.Text = $"Connected to {org.DisplayName}";
                ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("StatusModerate"));
                ApiTip.Text = "No users found. Org spending will still be tracked. You can select a user later.";
                ApiTip.Visibility = Visibility.Visible;
                SaveApiConsoleOrg(org);
            }
        }
        catch (Exception ex)
        {
            ApiSubtitle.Text = $"Connected to {org.DisplayName}";
            ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("StatusModerate"));
            ApiTip.Text = $"Could not load users: {ex.Message}. Org spending will still be tracked.";
            ApiTip.Visibility = Visibility.Visible;
            SaveApiConsoleOrg(org);
        }
        finally
        {
            ApiLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async Task EnrichOrgsWithSpend(List<APIOrganization> orgs, string sessionKey)
    {
        var tasks = orgs.Select(async org =>
        {
            try
            {
                var usage = await _apiService.FetchAPIUsageData(org.Id, sessionKey);
                org.SpendSummary = usage.SpendLimitCents > 0
                    ? $"{usage.FormattedUsed} of {usage.FormattedTotal}"
                    : usage.FormattedUsed;
            }
            catch { /* best-effort */ }
        });
        await Task.WhenAll(tasks);
    }

    // ── API Console Step 3: Confirm ──

    private void OnApiConfirm()
    {
        if (ApiUserCombo.SelectedItem is not ClaudeCodeUserMetrics user || _selectedOrg == null) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        SaveApiConsoleOrg(_selectedOrg);
        profile.ApiUserSearch = user.DisplayName;
        _settingsService.Save();

        ResetApiSetup();
        UpdateApiConnectedUI();
    }

    // ── Helpers ──

    private void SaveApiConsoleOrg(APIOrganization org)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        profile.ApiOrganizationName = org.Name;
        var credentials = _profileService.LoadCredentials(profile.Id);
        credentials.ApiSessionKey = _apiSessionKey;
        credentials.ApiOrganizationId = org.Id;
        _profileService.SaveCredentials(profile.Id, credentials);
        _settingsService.Save();

        _refreshCoordinator.InvalidateApiCache();
        _refreshCoordinator.RefreshNow();

        ResetApiSetup();
        UpdateApiConnectedUI();
    }

    private void ResetApiSetup()
    {
        ApiStep1.Visibility = Visibility.Visible;
        ApiStep2.Visibility = Visibility.Collapsed;
        ApiStep3.Visibility = Visibility.Collapsed;
        ApiTip.Visibility = Visibility.Collapsed;
        ApiKeyInput.Text = "";
        ApiSubtitle.Text = "Sign in to API Console to track Claude Code spending.";
        ApiSubtitle.Foreground = new SolidColorBrush(ThemeColors.Get("TextMuted"));
        _apiSessionKey = "";
        _selectedOrg = null;
    }

}
