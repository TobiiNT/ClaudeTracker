using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;

namespace ClaudeTracker.Views;

public partial class SetupWizardWindow : Window
{
    private readonly IClaudeApiService _apiService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private int _currentStep = 1;

    public SetupWizardWindow()
    {
        InitializeComponent();
        _apiService = App.Services.GetRequiredService<IClaudeApiService>();
        _profileService = App.Services.GetRequiredService<IProfileService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        SkipButton.Click += (_, _) => CompleteWizard();
        NextButton.Click += async (_, _) => await OnNextClicked();
    }

    private async Task OnNextClicked()
    {
        switch (_currentStep)
        {
            case 1:
                _currentStep = 2;
                Step1.Visibility = Visibility.Collapsed;
                Step2.Visibility = Visibility.Visible;
                NextButton.Content = "Connect";
                break;

            case 2:
                var key = WizardKeyInput.Text.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    WizardStatus.Text = "Please enter a session key";
                    return;
                }

                WizardProgress.Visibility = Visibility.Visible;
                NextButton.IsEnabled = false;
                WizardStatus.Text = "Testing connection...";

                try
                {
                    var orgs = await _apiService.TestSessionKey(key);
                    var org = orgs[0];

                    var profile = _profileService.ActiveProfile;
                    if (profile != null)
                    {
                        var creds = _profileService.LoadCredentials(profile.Id);
                        creds.ClaudeSessionKey = key;
                        creds.OrganizationId = org.Uuid;
                        _profileService.SaveCredentials(profile.Id, creds);
                    }

                    _currentStep = 3;
                    Step2.Visibility = Visibility.Collapsed;
                    Step3.Visibility = Visibility.Visible;
                    NextButton.Content = "Done";
                    SkipButton.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    WizardStatus.Text = $"Failed: {ex.Message}";
                }
                finally
                {
                    WizardProgress.Visibility = Visibility.Collapsed;
                    NextButton.IsEnabled = true;
                }
                break;

            case 3:
                CompleteWizard();
                break;
        }
    }

    private void CompleteWizard()
    {
        _settingsService.Settings.HasCompletedSetup = true;
        _settingsService.Save();
        Close();
    }
}
