using System.Windows;
using Microsoft.Web.WebView2.Core;
using ClaudeTracker.Services;

namespace ClaudeTracker.Views;

public partial class BrowserSignInWindow : Window
{
    private readonly string _targetUrl;
    private readonly string _cookieDomain;
    private readonly TaskCompletionSource<(string sessionKey, DateTime? expiry)?> _result = new();

    public Task<(string sessionKey, DateTime? expiry)?> ResultTask => _result.Task;

    public BrowserSignInWindow(string targetUrl, string cookieDomain)
    {
        InitializeComponent();
        _targetUrl = targetUrl;
        _cookieDomain = cookieDomain;
        Loaded += OnLoaded;
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch { return false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Utilities.Constants.WebView2.ProfilePath);
            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("WebView2 init failed", ex);
            StatusText.Text = "WebView2 initialization failed";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(_targetUrl);
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "sessionKey" && cookie.Domain.Contains(_cookieDomain))
                {
                    DateTime? expiry = cookie.Expires > DateTime.MinValue ? cookie.Expires.ToUniversalTime() : null;
                    StatusText.Text = "Session key captured!";
                    _result.TrySetResult((cookie.Value, expiry));
                    await Task.Delay(500); // Brief visual feedback
                    Dispatcher.Invoke(Close);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Cookie extraction failed", ex);
        }
    }
}
