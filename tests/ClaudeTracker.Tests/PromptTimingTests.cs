using ClaudeTracker.Models;
using ClaudeTracker.Views;
using Xunit;

namespace ClaudeTracker.Tests;

public class PromptTimingTests
{
    // --- GitHubStarPromptWindow.ShouldShow ---

    [Fact]
    public void StarPrompt_NewUser_NotShown()
    {
        var settings = new AppSettings { FirstLaunchDate = DateTime.UtcNow };
        Assert.False(GitHubStarPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void StarPrompt_AfterOneDay_Shown()
    {
        var settings = new AppSettings { FirstLaunchDate = DateTime.UtcNow.AddDays(-2) };
        Assert.True(GitHubStarPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void StarPrompt_AlreadyStarred_NotShown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-5),
            HasStarredGitHub = true
        };
        Assert.False(GitHubStarPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void StarPrompt_DismissedForever_NotShown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-5),
            StarPromptDismissedForever = true
        };
        Assert.False(GitHubStarPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void StarPrompt_RemindedRecently_NotShown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-5),
            LastStarPromptDate = DateTime.UtcNow.AddDays(-3) // within 10-day window
        };
        Assert.False(GitHubStarPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void StarPrompt_RemindExpired_Shown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-20),
            LastStarPromptDate = DateTime.UtcNow.AddDays(-11) // past 10-day window
        };
        Assert.True(GitHubStarPromptWindow.ShouldShow(settings));
    }

    // --- FeedbackPromptWindow.ShouldShow ---

    [Fact]
    public void FeedbackPrompt_NewUser_NotShown()
    {
        var settings = new AppSettings { FirstLaunchDate = DateTime.UtcNow };
        Assert.False(FeedbackPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void FeedbackPrompt_After7Days_Shown()
    {
        var settings = new AppSettings { FirstLaunchDate = DateTime.UtcNow.AddDays(-8) };
        Assert.True(FeedbackPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void FeedbackPrompt_AlreadySent_NotShown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-10),
            HasSentFeedback = true
        };
        Assert.False(FeedbackPromptWindow.ShouldShow(settings));
    }

    [Fact]
    public void FeedbackPrompt_DismissedForever_NotShown()
    {
        var settings = new AppSettings
        {
            FirstLaunchDate = DateTime.UtcNow.AddDays(-10),
            FeedbackPromptDismissedForever = true
        };
        Assert.False(FeedbackPromptWindow.ShouldShow(settings));
    }
}
