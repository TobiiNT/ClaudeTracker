using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClaudeTracker.Utilities;

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
                IconText.Foreground = new SolidColorBrush(ThemeColors.Get("AccentBlue"));
                break;
            case NotificationLevel.Warning:
                IconText.Text = "⚠";
                IconText.Foreground = new SolidColorBrush(ThemeColors.Get("StatusModerate"));
                break;
            case NotificationLevel.Critical:
                IconText.Text = "🔴";
                IconText.Foreground = new SolidColorBrush(ThemeColors.Get("StatusCritical"));
                break;
        }

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (_, _) => FadeOut();

        Loaded += OnLoaded;
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position using configured monitor + position
        var workArea = Utilities.PopupStackManager.GetWorkArea();
        var pos = Utilities.PopupStackManager.GetPosition();
        Left = pos.Contains("Left")
            ? workArea.Left + 16
            : workArea.Right - ActualWidth - 16;
        Top = pos.Contains("Bottom")
            ? workArea.Bottom - ActualHeight - 16
            : workArea.Top + 16;

        // Slide-in animation
        var isBottom = pos.Contains("Bottom");
        var slideAnim = new DoubleAnimation
        {
            From = isBottom ? Top + 40 : Top - 40,
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
