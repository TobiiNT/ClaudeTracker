using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string Version => Constants.AppVersion;
    public string GitHubUrl => Constants.GitHub.RepoUrl;

    [RelayCommand]
    private void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Constants.GitHub.RepoUrl,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenLogs()
    {
        var logDir = Path.GetDirectoryName(Constants.LogFilePath);
        if (logDir != null && Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
    }
}
