using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClaudeTracker.Views;

public partial class NotificationPopup : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public NotificationPopup(string title, string message, NotificationLevel level = NotificationLevel.Warning)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        switch (level)
        {
            case NotificationLevel.Info:
                IconText.Text = "ℹ";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                break;
            case NotificationLevel.Warning:
                IconText.Text = "⚠";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                break;
            case NotificationLevel.Critical:
                IconText.Text = "🔴";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                break;
        }

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (_, _) => FadeOut();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position at bottom-right of work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Bottom - ActualHeight - 16;

        // Slide-in animation
        var slideAnim = new DoubleAnimation
        {
            From = Top + 40,
            To = Top,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        BeginAnimation(TopProperty, slideAnim);
        BeginAnimation(OpacityProperty, fadeAnim);

        _autoCloseTimer.Start();
    }

    private void FadeOut()
    {
        _autoCloseTimer.Stop();

        var fadeAnim = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var slideAnim = new DoubleAnimation
        {
            From = Top,
            To = Top + 30,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeAnim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeAnim);
        BeginAnimation(TopProperty, slideAnim);
    }

    public event EventHandler? NotificationClicked;

    private void Body_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        NotificationClicked?.Invoke(this, EventArgs.Empty);
        FadeOut();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => FadeOut();

    public enum NotificationLevel
    {
        Info,
        Warning,
        Critical
    }
}
