using System.Windows;
using System.Windows.Controls;
using ClaudeTracker.Views.Settings;

namespace ClaudeTracker.Views;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    public static void ShowInstance()
    {
        if (_instance != null && _instance.IsVisible)
        {
            _instance.Activate();
            return;
        }

        _instance = new SettingsWindow();
        _instance.Show();
        _instance.Activate();
    }

    private SettingsWindow()
    {
        InitializeComponent();

        // Wire up navigation
        NavPersonal.Checked += (_, _) => ShowView<PersonalUsageView>();
        NavAppearance.Checked += (_, _) => ShowView<AppearanceView>();
        NavGeneral.Checked += (_, _) => ShowView<GeneralSettingsView>();
        NavProfiles.Checked += (_, _) => ShowView<ProfilesView>();
        NavHooks.Checked += (_, _) => ShowView<HooksSettingsView>();
        NavAbout.Checked += (_, _) => ShowView<AboutView>();

        // Show initial view
        ShowView<PersonalUsageView>();

        Closed += (_, _) => _instance = null;
    }

    private void ShowView<T>() where T : UserControl, new()
    {
        ContentArea.Content = new T();
    }
}
