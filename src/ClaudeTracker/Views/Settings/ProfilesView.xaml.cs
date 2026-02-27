using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Models;
using ClaudeTracker.ViewModels;

namespace ClaudeTracker.Views.Settings;

public partial class ProfilesView : UserControl
{
    private readonly ProfilesViewModel _vm;

    public ProfilesView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ProfilesViewModel>();
        DataContext = _vm;

        ProfileList.ItemsSource = _vm.Profiles;

        CreateButton.Click += (_, _) =>
        {
            var dialog = new InputDialog("New Profile", "Enter a name for the new profile:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                _vm.NewProfileName = dialog.ResultText;
                _vm.CreateProfileCommand.Execute(null);
            }
        };

        _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            ProfileList.ItemsSource = null;
            ProfileList.ItemsSource = _vm.Profiles;
        });
    }

    private void ProfileRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not Profile profile)
            return;

        var isActive = profile.Id == _vm.ActiveProfileId;

        // Find named elements within the DataTemplate
        var activeBadge = FindChild<Border>(border, "ActiveBadge");
        var activateButton = FindChild<Button>(border, "ActivateButton");
        var deleteButton = FindChild<Button>(border, "DeleteButton");

        if (activeBadge != null)
            activeBadge.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (activateButton != null)
            activateButton.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        if (deleteButton != null)
        {
            deleteButton.IsEnabled = !isActive;
            deleteButton.Opacity = isActive ? 0.3 : 1.0;
            deleteButton.ToolTip = isActive ? "Cannot delete the active profile" : "Delete profile";
        }
    }

    private void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
            _vm.ActivateProfileCommand.Execute(id);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            if (id == _vm.ActiveProfileId)
                return;

            var result = MessageBox.Show("Delete this profile?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                _vm.DeleteProfileCommand.Execute(id);
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;

            var result = FindChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }
}
