using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Views;

public partial class SetupWizardWindow : Window
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private readonly ClaudeCodeSyncService _cliSyncService;
    private int _currentStep = 1;

    public SetupWizardWindow()
    {
        InitializeComponent();
        _apiService = App.Services.GetRequiredService<IClaudeApiService>();
        _profileService = App.Services.GetRequiredService<IProfileService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _cliSyncService = App.Services.GetRequiredService<ClaudeCodeSyncService>();

        SkipButton.Click += (_, _) => CompleteWizard();
        NextButton.Click += async (_, _) => await OnNextClicked();

        // CLI auto-detect
        AutoDetectButton.Click += async (_, _) => await OnAutoDetect();

        // Browser sign-in (only if WebView2 available)
        if (BrowserSignInWindow.IsWebView2Available())
            BrowserCard.Visibility = Visibility.Visible;

        BrowserSignInButton.Click += async (_, _) => await OnBrowserSignIn();

        // Manual session key
        ConnectButton.Click += async (_, _) => await OnManualConnect();
    }

    private async Task OnNextClicked()
    {
        switch (_currentStep)
        {
            case 1:
                _currentStep = 2;
                Step1.Visibility = Visibility.Collapsed;
                Step2.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Collapsed;
                break;
            case 3:
                CompleteWizard();
                break;
        }
    }

    private async Task OnAutoDetect()
    {
        SetStatus("Detecting Claude Code credentials...", false);
        WizardProgress.Visibility = Visibility.Visible;

        try
        {
            var (token, orgUuid, subType, isExpired, expiresAt) = _cliSyncService.GetTokenInfo();

            if (string.IsNullOrEmpty(token))
            {
                SetStatus("No Claude Code credentials found. Make sure Claude Code is installed and logged in.", false);
                return;
            }

            if (isExpired)
            {
                SetStatus("CLI token is expired. Run 'claude' to refresh.", false);
                return;
            }

            var profile = _profileService.ActiveProfile;
            if (profile == null)
            {
                SetStatus("No active profile", false);
                return;
            }

            var success = _cliSyncService.SyncToProfile(_profileService, profile.Id);
            if (!success)
            {
                SetStatus("Failed to sync credentials", false);
                return;
            }

            var planLabel = !string.IsNullOrEmpty(subType) ? $" ({subType})" : "";
            SetStatus($"Connected via Claude Code{planLabel}", true);
            GoToDone();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            WizardProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task OnBrowserSignIn()
    {
        var window = new BrowserSignInWindow("https://claude.ai/login", "claude.ai");
        window.Owner = this;
        window.Show();
        var result = await window.ResultTask;

        if (result.HasValue)
        {
            SetStatus("Testing connection...", false);
            WizardProgress.Visibility = Visibility.Visible;

            try
            {
                var orgs = await _apiService.TestSessionKey(result.Value.sessionKey);
                var profile = _profileService.ActiveProfile;
                if (profile != null && orgs.Count > 0)
                {
                    profile.ClaudeSessionKey = result.Value.sessionKey;
                    profile.OrganizationId = orgs[0].Uuid;
                    if (result.Value.expiry.HasValue)
                        profile.ClaudeSessionKeyExpiry = result.Value.expiry;
                    _profileService.UpdateProfile(profile);

                    SetStatus($"Connected to {orgs[0].Name}", true);
                    GoToDone();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Failed: {ex.Message}", false);
            }
            finally
            {
                WizardProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async Task OnManualConnect()
    {
        var key = WizardKeyInput.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("Please enter a session key", false);
            return;
        }

        SetStatus("Testing connection...", false);
        WizardProgress.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = false;

        try
        {
            var orgs = await _apiService.TestSessionKey(key);
            var profile = _profileService.ActiveProfile;
            if (profile != null && orgs.Count > 0)
            {
                profile.ClaudeSessionKey = key;
                profile.OrganizationId = orgs[0].Uuid;
                _profileService.UpdateProfile(profile);

                SetStatus($"Connected to {orgs[0].Name}", true);
                GoToDone();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", false);
        }
        finally
        {
            WizardProgress.Visibility = Visibility.Collapsed;
            ConnectButton.IsEnabled = true;
        }
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

    private void SetStatus(string text, bool success)
    {
        WizardStatus.Text = text;
        WizardStatus.Foreground = success
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
    }

    private void CompleteWizard()
    {
        _settingsService.Settings.HasCompletedSetup = true;
        _settingsService.Settings.FirstLaunchDate ??= DateTime.UtcNow;
        _settingsService.Save();
        Close();
    }
}
