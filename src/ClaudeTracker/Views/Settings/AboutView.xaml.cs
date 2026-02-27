using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        var vm = App.Services.GetRequiredService<AboutViewModel>();
        DataContext = vm;

        VersionText.Text = $"Version {vm.Version}";
        GitHubButton.Click += (_, _) => vm.OpenGitHubCommand.Execute(null);
        LogsButton.Click += (_, _) => vm.OpenLogsCommand.Execute(null);
    }
}
