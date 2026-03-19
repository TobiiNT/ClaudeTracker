using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.TrayIcon;

namespace ClaudeTracker.Views;

public partial class SetupWizardWindow : Window
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private readonly IUsageRefreshCoordinator _refreshCoordinator;
    private readonly ClaudeCodeSyncService _cliSyncService;
    private readonly TrayIconManager _trayIconManager;
    private int _currentStep = 1;

    // API Console state
    private string _apiSessionKey = "";
    private List<APIOrganization> _apiOrgs = new();
    private APIOrganization? _selectedOrg;
    private bool _completedNormally;

    public SetupWizardWindow()
    {
        InitializeComponent();
        _apiService = App.Services.GetRequiredService<IClaudeApiService>();
        _profileService = App.Services.GetRequiredService<IProfileService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _refreshCoordinator = App.Services.GetRequiredService<IUsageRefreshCoordinator>();
        _cliSyncService = App.Services.GetRequiredService<ClaudeCodeSyncService>();
        _trayIconManager = App.Services.GetRequiredService<TrayIconManager>();

        SkipButton.Click += (_, _) => CompleteWizard();
        NextButton.Click += (_, _) => OnNextClicked();

        // Subscription plan
        AutoDetectButton.Click += async (_, _) => await OnAutoDetect();

        // API Console step 1: session key
        ConnectButton.Click += async (_, _) => await OnApiConnect();
        OpenConsoleButton.Click += (_, _) => OnOpenConsole();
        AutoSignInButton.Click += async (_, _) => await OnAutoSignIn();

        // API Console step 2: org selection
        ApiOrgCombo.PreviewMouseWheel += (s, e) => { if (s is ComboBox c && !c.IsDropDownOpen) e.Handled = true; };
        ApiOrgCombo.SelectionChanged += async (_, _) => await OnOrgSelected();

        // API Console step 3: user confirm
        ApiUserCombo.PreviewMouseWheel += (s, e) => { if (s is ComboBox c && !c.IsDropDownOpen) e.Handled = true; };
        ApiConfirmButton.Click += (_, _) => OnApiConfirm();

        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_completedNormally) return;

        var hasProgress = HasAnyProgress();
        if (hasProgress)
        {
            var result = MessageBox.Show(
                "You've connected some accounts. Save your progress?",
                "Setup In Progress",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    CompleteWizard();
                    e.Cancel = true; // CompleteWizard calls Close() itself
                    return;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    return;
                // No → fall through to shutdown
            }
        }

        // No progress or user chose No → exit app
        Application.Current.Shutdown();
    }

    private bool HasAnyProgress()
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return false;
        if (profile.HasAnyCredentials) return true;
        var creds = _profileService.LoadCredentials(profile.Id);
        return !string.IsNullOrEmpty(creds.ApiSessionKey);
    }

    // ── Navigation ──

    private void OnNextClicked()
    {
        switch (_currentStep)
        {
            case 1:
                _currentStep = 2;
                Step1.Visibility = Visibility.Collapsed;
                Step2.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Collapsed;
                SkipButton.Visibility = Visibility.Visible;
                break;
            case 2:
                GoToDone();
                break;
            case 3:
                CompleteWizard();
                break;
        }
    }

    private void ShowDoneButton()
    {
        SkipButton.Visibility = Visibility.Collapsed;
        NextButton.Content = "Done";
        NextButton.Visibility = Visibility.Visible;
    }

    private void GoToDone()
    {
        _currentStep = 3;
        Step2.Visibility = Visibility.Collapsed;
        Step3.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Visible;
        NextButton.Content = "Done";
        SkipButton.Visibility = Visibility.Collapsed;
    }

    private void CompleteWizard()
    {
        _completedNormally = true;
        _settingsService.Settings.HasCompletedSetup = true;
        _settingsService.Settings.FirstLaunchDate ??= DateTime.UtcNow;
        _settingsService.Save();
        Close();
        _trayIconManager.TogglePopover();
    }

    // ── Subscription Plan: CLI Auto-Detect ──

    private async Task OnAutoDetect()
    {
        OAuthSubtitle.Text = "Detecting...";
        WizardProgress.Visibility = Visibility.Visible;

        try
        {
            var (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();

            if (string.IsNullOrEmpty(token))
            {
                OAuthSubtitle.Text = "No credentials found. Run 'claude auth login' first.";
                return;
            }

            if (isExpired)
            {
                OAuthSubtitle.Text = "Token expired. Run 'claude' to refresh.";
                return;
            }

            var profile = _profileService.ActiveProfile;
            if (profile == null) return;

            var success = _cliSyncService.SyncToProfile(_profileService, profile.Id);
            if (!success)
            {
                OAuthSubtitle.Text = "Failed to sync credentials";
                return;
            }

            var planLabel = !string.IsNullOrEmpty(subType) ? $" ({subType})" : "";
            OAuthSubtitle.Text = $"Connected{planLabel} — fetching usage...";
            OAuthSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

            try { await _apiService.FetchUsageData(); } catch { /* non-fatal */ }

            OAuthSubtitle.Text = $"Connected{planLabel}";
            OAuthSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            AutoDetectButton.Content = "✓";
            AutoDetectButton.IsEnabled = false;
            ShowDoneButton();
        }
        catch (Exception ex)
        {
            OAuthSubtitle.Text = $"Error: {ex.Message}";
        }
        finally
        {
            WizardProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── API Console Step 1: Session Key ──

    private async Task OnAutoSignIn()
    {
        var window = new BrowserSignInWindow(
            Utilities.Constants.APIEndpoints.PlatformLogin, "platform.claude.com");
        window.Owner = this;
        window.Show();
        var result = await window.ResultTask;

        if (!result.HasValue) return;

        WizardKeyInput.Text = result.Value.sessionKey;
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
        var key = WizardKeyInput.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiSubtitle.Text = "Please enter a session key";
            return;
        }

        ApiSubtitle.Text = "Validating session key...";
        ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        ApiLoading.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = false;

        try
        {
            var orgs = await _apiService.FetchConsoleOrganizations(key);
            if (orgs.Count == 0)
            {
                ApiSubtitle.Text = "No organizations found.";
                return;
            }

            _apiSessionKey = key;
            _apiOrgs = orgs;

            // Step 1 complete
            ApiSubtitle.Text = "Fetching billing data...";
            ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            ApiStep1.Visibility = Visibility.Collapsed;

            // Enrich orgs with spend data (best-effort, don't block on failure)
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
                ApiOrgCombo.ItemsSource = orgs;
                ApiStep2.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ApiSubtitle.Text = $"Failed: {ex.Message}";
            ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }
        finally
        {
            ApiLoading.Visibility = Visibility.Collapsed;
            ConnectButton.IsEnabled = true;
        }
    }

    // ── API Console Step 2: Organization ──

    private async Task OnOrgSelected()
    {
        if (ApiOrgCombo.SelectedItem is not APIOrganization org) return;
        _selectedOrg = org;
        SaveApiConsoleOrg(org);
        await FetchUsersForOrg(org);
    }

    private async Task FetchUsersForOrg(APIOrganization org)
    {
        ApiLoading.Visibility = Visibility.Visible;
        ApiStep3.Visibility = Visibility.Collapsed;

        try
        {
            var users = await _apiService.FetchClaudeCodeAllUsers(org.Id, _apiSessionKey);
            if (users.Count > 0)
            {
                ApiUserCombo.ItemsSource = users;
                ApiStep2.Visibility = Visibility.Collapsed;
                ApiStep3.Visibility = Visibility.Visible;
            }
            else
            {
                ApiSubtitle.Text = $"Connected to {org.DisplayName}";
                ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                ShowApiTip("No users found. You can finish now to track org spending, and select a user later in Settings.");
                SaveApiConsoleOrg(org);
                ShowDoneButton();
            }
        }
        catch (Exception ex)
        {
            ApiSubtitle.Text = $"Connected to {org.DisplayName}";
            ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            ShowApiTip($"Could not load users: {ex.Message}. You can finish now to track org spending, and select a user later in Settings.");
            SaveApiConsoleOrg(org);
            ShowDoneButton();
        }
        finally
        {
            ApiLoading.Visibility = Visibility.Collapsed;
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
            catch { /* best-effort — leave SpendSummary null */ }
        });
        await Task.WhenAll(tasks);
    }

    private void ShowApiTip(string text)
    {
        ApiTip.Text = text;
        ApiTip.Visibility = Visibility.Visible;
    }

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
        _refreshCoordinator.RefreshNow();
    }

    // ── API Console Step 3: User Confirm ──

    private void OnApiConfirm()
    {
        if (ApiUserCombo.SelectedItem is not ClaudeCodeUserMetrics user || _selectedOrg == null) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        SaveApiConsoleOrg(_selectedOrg);
        profile.ApiUserSearch = user.DisplayName;
        _settingsService.Save();

        ApiStep3.Visibility = Visibility.Collapsed;
        ApiSubtitle.Text = $"Tracking: {user.DisplayName} @ {_selectedOrg.DisplayName}";
        ApiSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        ShowDoneButton();
    }
}
