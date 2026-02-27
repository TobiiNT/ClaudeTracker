using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeTracker.Services.Interfaces;
using ClaudeTracker.Utilities;

namespace ClaudeTracker.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;

    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isUpdateBusy;
    [ObservableProperty] private bool _isInstalled;

    public string Version => Constants.AppVersion;
    public string GitHubUrl => Constants.GitHub.RepoUrl;

    public AboutViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        _isInstalled = updateService.IsInstalled;
        SyncState();
        _updateService.StateChanged += OnUpdateStateChanged;
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(SyncState);
    }

    private void SyncState()
    {
        UpdateStatusText = _updateService.StatusText;
        IsUpdateAvailable = _updateService.IsUpdateAvailable;
        IsUpdateBusy = _updateService.IsBusy;
        IsInstalled = _updateService.IsInstalled;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await _updateService.CheckForUpdatesAsync();
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        await _updateService.ApplyUpdateAsync();
    }

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
