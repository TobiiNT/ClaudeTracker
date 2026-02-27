using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MaterialDesignThemes.Wpf;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class CliAccountView : UserControl
{
    private readonly CliAccountViewModel _vm;

    public CliAccountView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<CliAccountViewModel>();
        DataContext = _vm;

        SyncButton.Click += (_, _) => _vm.SyncCommand.Execute(null);

        _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(UpdateUI);
        UpdateUI();
    }

    private void UpdateUI()
    {
        TokenStatusText.Text = _vm.TokenStatus;
        ExpiresText.Text = _vm.ExpiresAtText;

        if (_vm.IsTokenValid)
        {
            StatusIcon.Kind = PackIconKind.CheckCircle;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else if (_vm.HasCli)
        {
            StatusIcon.Kind = PackIconKind.AlertCircle;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
        }
        else
        {
            StatusIcon.Kind = PackIconKind.InformationOutline;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
        }
    }
}
