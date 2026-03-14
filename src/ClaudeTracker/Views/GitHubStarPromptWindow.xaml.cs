using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.Views;

public partial class GitHubStarPromptWindow : Window
{
    public GitHubStarPromptWindow()
    {
        InitializeComponent();

        StarButton.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(Constants.GitHub.RepoUrl) { UseShellExecute = true });
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.HasStarredGitHub = true;
            settings.Save();
            Close();
        };

        RemindButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.LastStarPromptDate = DateTime.UtcNow;
            settings.Save();
            Close();
        };

        DontAskButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.StarPromptDismissedForever = true;
            settings.Save();
            Close();
        };
    }

    public static bool ShouldShow(Models.AppSettings settings)
    {
        if (settings.HasStarredGitHub || settings.StarPromptDismissedForever) return false;
        if (settings.FirstLaunchDate == null) return false;
        if ((DateTime.UtcNow - settings.FirstLaunchDate.Value).TotalDays < 1) return false;
        if (settings.LastStarPromptDate.HasValue &&
            (DateTime.UtcNow - settings.LastStarPromptDate.Value).TotalDays < 10) return false;
        return true;
    }
}
