using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class AboutView : UserControl
{
    private AboutViewModel? _vm;

    public AboutView()
    {
        InitializeComponent();
        var vm = App.Services.GetRequiredService<AboutViewModel>();
        _vm = vm;
        DataContext = vm;

        VersionText.Text = $"Version {vm.Version}";
        GitHubButton.Click += (_, _) => vm.OpenGitHubCommand.Execute(null);
        LogsButton.Click += (_, _) => vm.OpenLogsCommand.Execute(null);
        CheckUpdateButton.Click += (_, _) => vm.CheckForUpdatesCommand.Execute(null);
        ApplyUpdateButton.Click += (_, _) => vm.ApplyUpdateCommand.Execute(null);
        ReleaseNotesHyperlink.Click += (_, _) =>
        {
            if (vm.AvailableVersion is string ver)
                Process.Start(new ProcessStartInfo($"{Utilities.Constants.GitHub.RepoUrl}/releases/tag/v{ver}") { UseShellExecute = true });
        };
        CreditsLink.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/hamed-elfayome/Claude-Usage-Tracker") { UseShellExecute = true });

        vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateUpdateUI();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AboutViewModel.UpdateStatusText)
            or nameof(AboutViewModel.IsUpdateAvailable)
            or nameof(AboutViewModel.IsUpdateBusy)
            or nameof(AboutViewModel.IsInstalled))
        {
            UpdateUpdateUI();
        }
    }

    private void UpdateUpdateUI()
    {
        if (_vm == null) return;

        UpdatePanel.Visibility = _vm.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusText.Text = _vm.UpdateStatusText;
        UpdateSpinner.Visibility = _vm.IsUpdateBusy ? Visibility.Visible : Visibility.Collapsed;

        CheckUpdateButton.Visibility = !_vm.IsUpdateAvailable && !_vm.IsUpdateBusy
            ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = !_vm.IsUpdateBusy;

        ApplyUpdateButton.Visibility = _vm.IsUpdateAvailable
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyUpdateButton.IsEnabled = !_vm.IsUpdateBusy;

        ReleaseNotesLink.Visibility = _vm.IsUpdateAvailable
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
