using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using Constants = ClaudeTracker.Utilities.Constants;

namespace ClaudeTracker.Views;

public partial class FeedbackPromptWindow : Window
{
    private int _rating;
    private readonly Button[] _stars;

    public FeedbackPromptWindow()
    {
        InitializeComponent();
        _stars = new[] { Star1, Star2, Star3, Star4, Star5 };

        for (int i = 0; i < _stars.Length; i++)
        {
            var starIndex = i + 1;
            _stars[i].Click += (_, _) => SetRating(starIndex);
        }

        SubmitButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.FeedbackRating = _rating;
            settings.Settings.HasSentFeedback = true;
            settings.Save();

            if (Constants.Feedback.IsConfigured)
                _ = SendFeedbackAsync(_rating, CommentBox.Text);

            Close();
        };

        RemindButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.LastFeedbackPromptDate = DateTime.UtcNow;
            settings.Save();
            Close();
        };

        DontAskButton.Click += (_, _) =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            settings.Settings.FeedbackPromptDismissedForever = true;
            settings.Save();
            Close();
        };
    }

    private void SetRating(int rating)
    {
        _rating = rating;
        SubmitButton.IsEnabled = true;

        for (int i = 0; i < _stars.Length; i++)
        {
            var icon = (PackIcon)_stars[i].Content;
            icon.Kind = i < rating ? PackIconKind.Star : PackIconKind.StarOutline;
        }
    }

    private static async Task SendFeedbackAsync(int rating, string comment)
    {
        try
        {
            var factory = App.Services.GetService(typeof(System.Net.Http.IHttpClientFactory))
                as System.Net.Http.IHttpClientFactory;
            using var client = factory?.CreateClient("Claude") ?? new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Google Forms expects URL-encoded form data
            var formData = new Dictionary<string, string>
            {
                [Constants.Feedback.EntryRating] = rating.ToString(),
                [Constants.Feedback.EntryComment] = comment ?? "",
                [Constants.Feedback.EntryVersion] = Constants.AppVersion
            };
            var content = new System.Net.Http.FormUrlEncodedContent(formData);
            await client.PostAsync(Constants.Feedback.SubmitUrl, content);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to send feedback", ex);
        }
    }

    public static bool ShouldShow(Models.AppSettings settings)
    {
        if (settings.HasSentFeedback || settings.FeedbackPromptDismissedForever) return false;
        if (settings.FirstLaunchDate == null) return false;
        if ((DateTime.UtcNow - settings.FirstLaunchDate.Value).TotalDays < Constants.Feedback.PromptAfterDays) return false;
        if (settings.LastFeedbackPromptDate.HasValue &&
            (DateTime.UtcNow - settings.LastFeedbackPromptDate.Value).TotalDays < Constants.Feedback.RemindIntervalDays) return false;
        return true;
    }
}
